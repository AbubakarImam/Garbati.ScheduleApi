# Garbati.ScheduleApi

GARBATI's standing-orders / recurring-payments service, built with ASP.NET Core 8, EF Core and
PostgreSQL. It owns no money and no authorization — a scheduled run is submitted to
[`Garbati.OrchestratorApi`](https://github.com/AbubakarImam/Garbati.OrchestratorApi) exactly like any
other channel's request, going through the same authorization and posting path an interactive transfer
does. ScheduleApi's own job is narrower: remember what's supposed to recur, decide when it's due, and
record what happened when it ran.

The design, including every trade-off, is in [docs/DESIGN.md](docs/DESIGN.md) — start there for the
*why*. This README covers the *how*. See [`garbati-platform/ARCHITECTURE.md`](https://github.com/AbubakarImam/garbati-platform/blob/main/ARCHITECTURE.md)
for how this service fits the rest of the platform.

## What this build covers

This is ScheduleApi's **first** build task (`garbati-platform` T-071) and its entire v1 scope in one go —
`docs/DESIGN.md`'s whole point is that v1 targets GARBATI's full Fineract-equivalent standing-instruction
feature set, honestly adapted to what GARBATI actually has today (no financing/loan module yet):

- `StandingInstruction`/`StandingInstructionRun` entities (`docs/DESIGN.md` Section 2) — every
  Fineract axis modeled; everything requiring a loan rejected clearly at creation (422) until a
  financing module exists.
- All 7 endpoints under `/v1/standing-instructions` (Section 4): create, get, paged list, real in-place
  edit, soft delete, pause/resume, paged run history.
- A plain `BackgroundService` polling hourly (Section 3) — no Quartz, no Hangfire. Computes the
  `sched:{id}:{date}` idempotency key, applies the narrow `AllowPartialTransfer` balance check, submits
  to OrchestratorApi, records the outcome. No auto-retry of a `Failed` run.
- Two downstream clients: `OrchestratorApiClient` (the real, existing `POST /v1/transfers`) and
  `LedgerApiClient` (the real, existing `GET /api/accounts/{accountNumber}/balance`, the one narrow D-33
  exception). Neither sibling repo was changed.

It deliberately does **not** make any loan-requiring field functional, enforce `Priority` for ordering,
or build any staff/ops-console UI. See `docs/DESIGN.md` Section 5 for exactly why, and Appendix A for
this build's own judgment calls.

## Core pieces

- **StandingInstruction** — the recurring configuration itself: `StandingInstructionId` (public,
  generated), the five Fineract-sourced axes (`FromAccountType`/`ToAccountType`/`TransferType`/
  `InstructionType`/`RecurrenceType`), the schedule (`ValidFrom`/`ValidTill`/`RecurrenceFrequency`/
  `RecurrenceInterval`/`RecurrenceOnDay`), `Status` (`Active`/`Disabled`/`Deleted`), `LastRunDate`.
- **StandingInstructionRun** — one row per attempted run: `ScheduledDate`, `Status` (`Success`/
  `Partial`/`Failed`/`Skipped`), the actual `Amount` moved, `IdempotencyKey`, `OrchestratorReference`.
- **Scheduling/** — `StandingInstructionSchedulerService` (the `BackgroundService` itself),
  `RecurrenceSchedule` (pure due-date/clamping logic, directly unit-testable), `StandingInstructionRunner`
  (one tick's worth of work: find due instructions, call the downstream clients, record the outcome).
- **Clients/** — this service as a client of OrchestratorApi and LedgerApi: `IOrchestratorApiClient`/
  `ILedgerApiClient`, backed by `IHttpClientFactory`-managed `HttpClient`s carrying this service's own
  `X-Api-Key` to each, propagating `X-Correlation-ID` downstream.
- **Validation/EnumParser** — every enum-shaped request/response field is a plain string on the wire,
  parsed via this one generic helper (the `LedgerApi` PR #31 lesson: a raw C# enum with no
  `JsonStringEnumConverter` only accepts the ordinal integer, not the string a real caller sends).

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (pinned by `global.json`)
- [Docker](https://www.docker.com/) (for the local database and for the tests)

## Getting started

**1. Start PostgreSQL.** The Development connection string expects it on port `5438` (see
`docker-compose.yml`'s own port list in `garbati-platform` for every sibling service's port):

```bash
docker run -d --name scheduleapi-postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=scheduleapi -p 5438:5432 postgres:16-alpine
```

**2. Apply the migrations.** `dotnet-ef` is pinned in the repo's tool manifest:

```bash
dotnet tool restore
dotnet ef database update --project src/Garbati.ScheduleApi
```

**3. Point it at real OrchestratorApi/LedgerApi instances.** Edit
`src/Garbati.ScheduleApi/appsettings.Development.json`'s `DownstreamServices` section with their actual
base URLs and the API keys they've issued this service (see each sibling's own README for how to issue
one with the right scopes — this service needs `orchestrator.transfers` from OrchestratorApi and
`ledger.read` from LedgerApi). **Out of scope for T-071** (see `docs/DESIGN.md`'s "What this design
explicitly does not do"): neither sibling repo has had a matching client added for ScheduleApi, so the
placeholder keys in `appsettings.Development.json` do not actually authenticate against a real,
locally-run OrchestratorApi/LedgerApi yet — that is real infrastructure work for whenever this service
actually needs to talk to them live, same as T-048 was its own separate task for every other service.

**4. Run the API:**

```bash
dotnet run --project src/Garbati.ScheduleApi --launch-profile http
```

Swagger UI is at the printed `http://localhost:{port}/swagger` (click **Authorize** and paste
`dev-schedule-driver-key` — the plaintext behind the dev client configured in
`appsettings.Development.json`).

## Running the tests

```bash
dotnet test
```

146 tests, all against a real PostgreSQL container started by
[Testcontainers](https://dotnet.testcontainers.org/), so Docker must be running. `IOrchestratorApiClient`/
`ILedgerApiClient` are replaced with hand-rolled fakes in every automated test (same judgment call
`OrchestratorApi`'s own Appendix A made — see `docs/DESIGN.md` Appendix A judgment call #7) — no real
OrchestratorApi/LedgerApi instance is needed to run this suite, and none is contacted. The
`StandingInstructionSchedulerService` hosted service is disabled under the `Testing` environment so no
real background tick races a test's own data; `Scheduling/StandingInstructionRunnerTests` exercises that
logic directly instead, against a real database.

## Endpoints

All under `/v1/standing-instructions` (`docs/DESIGN.md` Section 4):

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/` | Create. Requires a client-supplied `idempotencyKey` (see [Idempotency](#idempotency) below). `422` for any not-yet-supported combination (`Loan` account type, non-`AccountTransfer`, `Dues`, `AsPerDues`) with a clear reason; `409` for a duplicate `Name` or a reused `idempotencyKey` with a different body |
| `GET` | `/{id}` | Current state (any status, including `Deleted`) |
| `GET` | `?customerId=` | Paged list for one customer |
| `PUT` | `/{id}` | Real in-place edit: amount, schedule, priority, `AllowPartialTransfer`. `422` on a `Deleted` instruction |
| `DELETE` | `/{id}` | Soft delete (`Status` → `Deleted`) — idempotent, not a row removal |
| `PATCH` | `/{id}/status` | `Active` ↔ `Disabled` only. `422` on a `Deleted` instruction; `400` for any other value (including the literal `"Deleted"`) |
| `GET` | `/{id}/runs` | Paged run history, most-recent-first |

### Status codes

| Status | Meaning |
|---|---|
| `200` | Success |
| `400` | Request failed validation (including an unrecognized enum-shaped string) |
| `401` | Missing or unknown API key |
| `403` | The API key lacks the endpoint's scope |
| `404` | Standing instruction not found |
| `409` | Duplicate `Name`, or `idempotencyKey` reused with a different request body |
| `422` | A not-yet-supported financing combination at creation/edit, or any write attempt against a `Deleted` instruction |
| `500` | Unexpected error |

## Idempotency

`POST /v1/standing-instructions` requires a client-supplied `idempotencyKey` (1-128 characters: letters,
digits, `-`, `_`, `.` or `:`, and must not start with the reserved `sched:` prefix - that namespace is
reserved for this service's own derived run keys, `sched:{id}:{date}`), scoped to the calling API
client - the same scheme `LedgerApi`'s `POST /api/accounts` and `Garbati.IdentityApi`'s
`POST /v1/customers` use. A retry with the same key and the same other fields returns the original
instruction (`200`), not a second one; the same key with different fields gets `409`.

This was a real gap in `docs/DESIGN.md`'s original entity table, found during this service's first
build and fixed before merge - see `docs/DESIGN.md` Section 6 decision #8 and Appendix A.

## Authentication

Same pattern as every sibling service (`ARCHITECTURE.md` Section 5): every request needs an
`X-Api-Key` header, and each key is granted **scopes**:

| Scope | Endpoints |
|---|---|
| `scheduleapi.read` | `GET /{id}`, `GET ?customerId=`, `GET /{id}/runs` |
| `scheduleapi.write` | `POST /`, `PUT /{id}`, `DELETE /{id}`, `PATCH /{id}/status` |

A missing or unknown key gets **401**; a valid key without the endpoint's scope gets **403**. This is a
separate concern from the credentials this service presents *to* OrchestratorApi/LedgerApi as their
client — see `DownstreamServices` in [Getting started](#getting-started).

### Development keys

`appsettings.Development.json` configures one local-only client. Never use this outside your machine.

| Client | Plaintext key (dev only) | Scopes |
|---|---|---|
| `dev-schedule-driver` | `dev-schedule-driver-key` | `scheduleapi.read`, `scheduleapi.write` |

### Issuing a key

Only the SHA-256 hash of a key is configured, never the key itself:

```bash
KEY="sdk_$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')"; echo "$KEY"; printf %s "$KEY" | openssl dgst -sha256 -binary | base64
```

## The scheduling mechanism

A plain `BackgroundService` (`Scheduling/StandingInstructionSchedulerService`) polls hourly — no new
dependency. Each tick (`Scheduling/StandingInstructionRunner`):

1. Finds every `Active` instruction due today (`Scheduling/RecurrenceSchedule` — `Days`/`Weeks`/
   `Months`/`Years`, clamped to the target month's real length for `Months`/`Years`) whose `LastRunDate`
   is before today.
2. Computes `sched:{internal id}:{scheduledDate:yyyy-MM-dd}` as the idempotency key.
3. If `AllowPartialTransfer` is `true`, checks the real balance via `LedgerApiClient` first; otherwise
   skips that call entirely and submits the full amount.
4. Submits to `OrchestratorApiClient.SubmitTransferAsync` and records a `StandingInstructionRun` —
   `Success`/`Partial` on success, `Failed` with the error message on rejection, `Skipped` if nothing was
   ever submitted (e.g. a zero/negative balance under `AllowPartialTransfer`).
5. Updates `LastRunDate` regardless of outcome — a same-day second tick is a no-op, and a `Failed` run
   is never retried before the instruction's own next naturally-scheduled occurrence.

## Logging

Every request gets a correlation id (`X-Correlation-ID`), propagated to every downstream call this
service makes to OrchestratorApi/LedgerApi so one operation's logs line up across all three services.
This is deliberately all GARBATI ships today — no metrics/tracing/alerting stack. See
`garbati-platform`'s `OBSERVABILITY.md` for why.

## Deployment

The `Dockerfile` builds two images:

```bash
docker build -t scheduleapi .                               # the API
docker build --target migrator -t scheduleapi-migrator .    # applies pending migrations, then exits
```

GitHub Actions (`.github/workflows/ci.yml`) runs the tests and builds both images on every pull request
and push to `main`. There is no `deploy` job yet — this service has no App Service, database server or
federated credential set up.

## Project layout

```
src/Garbati.ScheduleApi/
  Controllers/      HTTP endpoints (StandingInstructionsController)
  Services/         StandingInstructionService (CRUD business logic), id/page-token generation
  Scheduling/        The BackgroundService, RecurrenceSchedule, StandingInstructionRunner
  Clients/          This service as a client of OrchestratorApi/LedgerApi:
                      Interfaces/, Implementations/ (HTTP clients), Contracts/ (downstream DTOs)
  Entities/         StandingInstruction, StandingInstructionRun, and every enum
  Data/             ScheduleDbContext and model configuration
  Migrations/       EF Core migrations
  Validation/       Request validators, EnumParser, ScheduleFieldsValidator
  Middleware/       Correlation id, exception-to-status mapping
  Authorization/    API-key authentication and scopes (for callers of THIS service)
  Configuration/    DownstreamServicesOptions (this service's own outbound credentials)
  Contracts/        This service's own request/response records
  Exceptions/
tests/Garbati.ScheduleApi.Tests/
  Controllers/      Real HTTP-level tests for all 7 endpoints, against PostgreSQL (Testcontainers)
  Scheduling/       RecurrenceSchedule (pure logic) and StandingInstructionRunner (end to end,
                     real database, fake downstream clients) tests
  Clients/          OrchestratorApiClient/LedgerApiClient tests against a fake HttpMessageHandler
  Authorization/, Configuration/, Data/, Validation/, Fixtures/
docs/DESIGN.md      Design document, decisions log, and Appendix A (T-071 build notes)
```

## Out of scope

No financing-module functionality (every loan-requiring field/enum value is modeled but rejected
clearly). No fund-contention priority ordering. No staff/ops-console UI. No real production API keys for
the two downstream clients. See `docs/DESIGN.md` Section 5 and "What this task does not include" in the
task file for the full list and why.
