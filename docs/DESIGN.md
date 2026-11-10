# Garbati.ScheduleApi — Design Document (v1)

**Status:** Design v1 — ratified and **built** (`garbati-platform` T-071, `feature/T-071-foundation`, 10 November 2026 — see Appendix A for build notes and judgment calls).
**Last updated:** 10 November 2026
**Service name:** `ScheduleApi` (`Garbati.ScheduleApi`)
**Platform:** Garbati — see [`garbati-platform/ARCHITECTURE.md`](https://github.com/AbubakarImam/garbati-platform/blob/main/ARCHITECTURE.md) Section 6 (service map), Section 4.3 (standing-order flow), decision P-3, and Section 9's "Standing order retry-on-failure" entry — this document is the detailed design behind that one-line placement; it does not repeat P-3's own reasoning, only extends it.
**Decision history:** `garbati-platform/TASKS.md` D-2 (P-3's placement, separate service calling the orchestrator only), D-33 (this design's three real open questions, decided 10 Nov 2026), `research/architecture-patterns-p1-p6.md` (T-011), `research/fineract-core-functionality-gap-analysis.md` (T-059, Area 7 — the two-part `standinginstructions`/`standinginstructionrunhistory` split), `research/fineract-standing-instructions-detail.md` (T-069 — the full feature-set deep dive this design targets).

## 1. Purpose and scope

ScheduleApi owns **recurring instructions and their run history** (`ARCHITECTURE.md`'s own one-line
scope statement, Section 6). It owns no money and no authorization — a scheduled run is submitted to
`OrchestratorApi` exactly like any other channel's request, going through the exact same authorization
and posting path an interactive transfer does. ScheduleApi's only job is: remember what's supposed to
recur, decide when it's due, and record what happened when it ran.

### 1.1 Why target full Fineract-equivalent functionality (owner's explicit choice, D-33)

Apache Fineract's real, shipped standing-instruction model (confirmed directly from its own source and
database schema, `research/fineract-standing-instructions-detail.md`, T-069) is a genuinely rich
system: a standing instruction is a thin, recurring, scheduled wrapper around the *same*
`AccountTransferDetails`/`AccountTransferType` object an ordinary one-off transfer uses — five real
transfer types, Savings/Loan independently on each side, a fixed-or-dues amount, periodic-or-dues-linked
recurrence, real in-place editing, a soft-delete lifecycle, and a real `allowPartialTransfer` feature.
This design targets that full shape, honestly adapted to what GARBATI actually has today: **GARBATI has
no financing/loan module yet** (`ProductApi` is deposit-only; the financing module itself is gated on a
qualified Sharia scholar's sign-off, D-28). Everything Fineract's model needs a loan for is modeled in
this design's data shape — so nothing has to be redesigned once financing exists — but is **rejected
clearly at creation time** today, the same "fails cleanly, not silently" discipline `OrchestratorApi`'s
own `ProductCode` mapping already uses for an unmapped jurisdiction.

### 1.2 What v1 actually does, end to end

A customer (today: a caller-supplied `CustomerId`, the same unauthenticated-channel honesty
`OrchestratorApi`'s own `TransferRequest.RequestedBy` already has) creates a `StandingInstruction`
specifying a debit account, a credit account, an amount, and a recurrence rule. A background process
inside ScheduleApi checks, on its own schedule, which instructions are due. For each due instruction, it
submits an ordinary transfer request to `OrchestratorApi` — the same call any channel makes — with a
deterministic idempotency key, and records a `StandingInstructionRun` row with the outcome.

## 2. Core entities

### 2.1 `StandingInstruction`

| Field | Type | Notes |
|---|---|---|
| `Id` | long | internal PK |
| `StandingInstructionId` | string | public id, same internal-long/public-string split every other GARBATI entity uses |
| `Name` | string, **unique** | required — matches Fineract's own real, confirmed `name` field (not in the task's original guessed field list; found by T-069) |
| `CustomerId` | string | the owner; GARBATI's own addition, since Fineract's "client" concept doesn't map 1:1 — no mandate/authorization check happens here, that's `IdentityApi`'s job on each individual run (3.3) |
| `FromAccountType` | enum: `Savings`, `Loan` | **v1: only `Savings` is accepted at creation.** `Loan` is modeled for schema parity (Fineract's own real axis, confirmed independently nullable on each side of its `m_account_transfer_details` table) and rejected with a clear `NoFinancingModuleException`-style 422 until a financing module exists |
| `FromAccountId` | string | a `LedgerApi` account number |
| `ToAccountType` | enum: `Savings`, `Loan` | same v1 restriction as `FromAccountType` |
| `ToAccountId` | string | a `LedgerApi` account number |
| `TransferType` | enum: `AccountTransfer`, `LoanRepayment`, `ChargePayment`, `InterestTransfer`, `LoanDownPayment` | **v1: only `AccountTransfer` is accepted.** The other four (Fineract's real, confirmed enum, T-069 Finding 1) all require a loan on one side; modeled now, rejected clearly at creation until financing exists |
| `InstructionType` | enum: `Fixed`, `Dues` | **v1: only `Fixed` is accepted.** `Dues` ("whatever is currently owed") requires a linked loan's own repayment schedule — rejected clearly |
| `Priority` | enum: `Urgent`, `High`, `Medium`, `Low` | modeled for parity (Fineract's own real field, T-069 Finding 6); **not enforced for fund-contention ordering in v1** — T-069's own research could not confirm, even in Fineract's real shipped code, that priority actually governs execution order when several instructions compete for one short-funded account. Runs are processed in a simple, deterministic order (ascending `StandingInstructionId`) instead of inventing an ordering guarantee neither this design nor Fineract's own confirmed behavior backs up |
| `Amount` | decimal | the configured amount for a `Fixed` instruction |
| `Currency` | string | one of LedgerApi's supported currencies |
| `Narration` | string, nullable | passed through to each run's transfer |
| `ValidFrom` | date | required — the first date this instruction can run |
| `ValidTill` | date, nullable | **null means no end date** ("until further notice") — matches Fineract's own confirmed nullable `valid_till`. **No occurrence-count field** — T-069 confirmed Fineract itself has no "stop after N runs" mechanism either; an instruction runs until `ValidTill` (if set) or until cancelled |
| `RecurrenceType` | enum: `Periodic`, `AsPerDues` | **v1: only `Periodic` is accepted.** `AsPerDues` (fire whenever something is owed on a linked loan) is rejected clearly |
| `RecurrenceFrequency` | enum: `Days`, `Weeks`, `Months`, `Years` | |
| `RecurrenceInterval` | int | e.g. every 2 weeks |
| `RecurrenceOnDay` | int, nullable | day of month (`Months`/`Years`) or day of week (`Weeks`) — **clamped, not truncated, to the target month's real length** (see 4.2 — this is the FINERACT-2914 lesson, T-069 Finding 2, baked in deliberately, not left to be rediscovered) |
| `AllowPartialTransfer` | bool | real, functional in v1 (3.2) — Fineract's own real, shipped feature (T-069 Finding 5) |
| `Status` | enum: `Active`, `Disabled`, `Deleted` | soft lifecycle, matching Fineract's own real three-value enum exactly (T-069 Finding 4) |
| `LastRunDate` | date, nullable | the most recent date a run was attempted (success or failure — this is "last attempted," not "last succeeded") |
| `CreatedAt`, `UpdatedAt` | timestamp | |
| `IdempotencyKey` | string, nullable | **added by T-071, a gap in this table, not a deliberate omission** — every other create endpoint on this platform (`LedgerApi`'s `POST /api/accounts`, `Garbati.IdentityApi`'s `POST /v1/customers`) takes a client-supplied idempotency key, scoped per calling client, so a network retry transparently replays the original resource instead of creating a duplicate or erroring. This table originally had no such field at all; T-071 found this while building against the real sibling patterns and fixed it rather than silently matching an incomplete table (see Appendix A) |
| `InitiatedBy` | string, nullable | the calling client id (added alongside `IdempotencyKey`) — together they form the same unique `(InitiatedBy, IdempotencyKey)` index every sibling service's equivalent entity uses |
| `RequestHash` | string, nullable | fingerprint of the fields that define what gets created, so a reused key with a different body is detected and refused (409) rather than silently replaying the wrong instruction — same convention as `LedgerApi`'s `AccountService.HashRequest`/`Garbati.IdentityApi`'s `CustomerService.HashRequest` |

### 2.2 `StandingInstructionRun`

| Field | Type | Notes |
|---|---|---|
| `Id` | long | internal PK |
| `StandingInstructionId` | long | FK |
| `ScheduledDate` | date | the date this run was due for — **this, not `Id`, is half of the idempotency key (3.1)** |
| `Status` | enum: `Success`, `Partial`, `Failed`, `Skipped` | plain, named values — Fineract's own real run-history `status` column is a free string whose exact literal values T-069 could not confirm from source, so this design defines its own clean, closed set rather than guess at Fineract's |
| `Amount` | decimal | the amount actually transferred — may be less than `StandingInstruction.Amount` on a `Partial` run |
| `ExecutedAt` | timestamp | |
| `ErrorMessage` | string, nullable | set on `Failed`; also set, non-fatally, on a `Partial` run's underlying "funds were short" reason |
| `IdempotencyKey` | string | `sched:{standingInstructionId}:{scheduledDate:yyyy-MM-dd}` (3.1) |
| `OrchestratorReference` | string, nullable | the resulting transfer's own reference from `OrchestratorApi`, for traceability — null on a `Skipped` run (nothing was ever submitted) |

`Skipped` (not a Fineract-sourced value, a GARBATI addition): a run that was due but never submitted at
all — e.g. the instruction was `Disabled`/`Deleted` by the time the scheduler reached it, or
`ValidTill` had already passed. Distinct from `Failed` (a run that *was* submitted and came back
rejected) so the run history can honestly answer "did we even try."

## 3. The scheduling mechanism

### 3.1 How a run is triggered and kept idempotent

A plain ASP.NET Core `BackgroundService` — **no new dependency** (no Hangfire, no Quartz). T-069
confirmed Fineract's own mechanism is a real, named scheduled job (`ExecuteStandingInstructionsTasklet`)
on a general-purpose job scheduler (Quartz) — GARBATI has no equivalent anywhere yet (confirmed
independently by T-059), and a plain polling loop is the simplest sufficient mechanism for v1, matching
this project's own repeated "simplest sufficient tool" discipline (e.g. `StaffIdentityApi`'s choice of
`PasswordHasher` over a heavier identity framework).

The loop polls on a fixed interval (an hourly tick is enough to catch every due date reliably without
needing second-level precision) for every `Active` instruction where `ValidFrom <= today <= (ValidTill
?? today)` and today matches the recurrence rule, and `LastRunDate < today` (so a tick that fires twice
on the same day is a no-op, not a double-submission — defence in depth; the idempotency key below is
the real guard).

**Idempotency key** (already named in `ARCHITECTURE.md` 4.3, confirmed and kept exactly):
`sched:{standingInstructionId}:{scheduledDate:yyyy-MM-dd}`. A repeated trigger for the same instruction
on the same calendar date — a restart, a double tick — submits the identical key to `OrchestratorApi`,
which (via its own existing idempotency mechanism) cannot pay twice. This mirrors the same
system-generated, caller-unreachable key namespace already used for chain-leg keys
(`chain:{chainId}:{sequence}`, `LedgerApi` decision #45) and account-opening step keys.

### 3.2 `AllowPartialTransfer` (D-33, the narrow `LedgerApi` exception)

When `AllowPartialTransfer` is `true` and the configured `Amount` would exceed what the `FromAccountId`
can actually pay, the run should move whatever's affordable rather than fail outright — this is a real,
deliberately-chosen Fineract feature (T-069 Finding 5), not an invented one.

**ScheduleApi queries `LedgerApi`'s existing `GET /api/accounts/{accountNumber}/balance` directly**
(owner's explicit call, D-33) — a narrow, read-only exception to `ARCHITECTURE.md`'s general "Channels
and ScheduleApi call only the orchestrator" framing, mirroring `OpsApi`'s own existing precedent (it
calls `LedgerApi`'s funding endpoint directly, since a deposit carries no authorization question). A
balance enquiry is read-only, carries no authorization question either, and `OrchestratorApi` has no
balance-query endpoint to proxy it through. `ScheduleApi` needs the `ledger.read` scope on its own
service-to-service key for exactly this one call — nothing else it does touches `LedgerApi` directly.

Mechanically: before submitting a run, if `AllowPartialTransfer` is `true`, call the balance endpoint;
if the real balance is less than `Amount`, submit the transfer for the lesser of the two (or skip
entirely if the balance is zero or negative, recording `Skipped` with a clear reason) and record the
run as `Partial` with the actual amount moved. If `AllowPartialTransfer` is `false` (the default) and
the full amount can't be confirmed affordable, just submit the full amount to `OrchestratorApi` as
normal and let its own insufficient-funds rejection produce a `Failed` run — no balance pre-check needed
in the common case, keeping the narrow `LedgerApi` exception genuinely narrow (only exercised when a
caller has opted in to partial transfers).

### 3.3 What happens to a failed run (D-33, resolving `ARCHITECTURE.md` Section 9's P-3 question)

**No auto-retry.** A `Failed` run is recorded with its error message and left alone; the instruction's
own next naturally-scheduled occurrence is the next attempt. This matches the real-world convention
T-011's own research found (a standing order, unlike a direct debit a payee's bank can re-present, is
traditionally not auto-retried) and matches Fineract's own real, shipped, bug-fixed behavior exactly
(T-069 Finding 5: a 2026-era Fineract bug fix, PR #6806, moved *away* from a worse behavior — one
failure rolling back an entire batch — to today's per-instruction isolation with no automatic retry of
the failed instruction before its own next occurrence). GARBATI's choice is the same as Fineract's real,
current, confirmed answer, not merely the same as the generic real-world convention.

### 3.4 Month-day recurrence, clamped (the FINERACT-2914 lesson)

A `Months`/`Years` instruction's `RecurrenceOnDay` is **clamped to the target month's actual last day**,
never truncated or allowed to throw. A day-31 instruction due in April (30 days) runs on April 30, not
"crashes the whole scheduler tick" and not "silently never runs in any 30-day month" — T-069 confirmed
Fineract shipped and later fixed *both* of those exact failure modes in production (FINERACT-2914) before
arriving at clamping as the real fix. GARBATI starts from the fixed behavior, not the original bug.

**Implementation note added by T-071 (deviation from the task file's own summary, not from this design):**
T-071's task file, when describing this due-date check, mentions respecting
"`RecurrenceFrequency`/`RecurrenceInterval`/`RecurrenceOnDay`/`RecurrenceOnMonth`" — but Section 2.1's own
`StandingInstruction` field table above, which this document treats as authoritative, defines no
`RecurrenceOnMonth` field at all, only `RecurrenceOnDay`. T-071 read the table literally rather than
inventing a field this design never defined: a `Years`-frequency instruction's month is anchored to
`ValidFrom`'s own month (so a `Years` instruction created on 15 March recurs every N years in March,
clamped to that month's own length the same way `Months` is). If a separately configurable recurrence
month is ever wanted, that is a new field for a future design revision, not something T-071 silently
added. Separately, since Section 2.1 doesn't specify a numbering convention for `RecurrenceOnDay` as a
day of week (`Weeks`), T-071 uses ISO 8601 (`1` = Monday … `7` = Sunday).

### 3.5 Joint accounts (D-33)

**v1 scopes standing instructions to single-holder accounts only.** `IdentityApi`'s authorization
service grants a single-holder account's request immediately, based on mandate/account state, with no
live human approval needed at call time — exactly the shape an unattended scheduled run needs. A
joint account (`RequiredApprovals > 1`) genuinely cannot work this way: nobody is present at 3am to
cast an M-of-N approval. **This is deliberately not pre-validated at creation** — confirmed directly
against `IdentityApi`'s real code (`AuthorizationService.RequestAuthorizationAsync`), a request against
a joint account already gets a clean, correctly-structured denial (`ApprovalPolicyNotSingleHolder`),
not a crash and not a security hole. A standing instruction created against a joint account simply fails
cleanly on its first scheduled run, recorded as `Failed` with that exact reason — the existing
authorization chain already does the right thing without ScheduleApi needing to know anything about
mandate policy itself.

## 4. Endpoints (v1)

All under `/v1/standing-instructions`, service-to-service authenticated, same `X-Api-Key`/scope
pattern every sibling service uses.

| Method | Path | Scope | Notes |
|---|---|---|---|
| `POST` | `/v1/standing-instructions` | `scheduleapi.write` | Create. Rejects `FromAccountType`/`ToAccountType = Loan`, `TransferType != AccountTransfer`, `InstructionType != Fixed`, `RecurrenceType != Periodic` with a clear 422 naming the exact reason (no financing module exists yet) |
| `GET` | `/v1/standing-instructions/{id}` | `scheduleapi.read` | |
| `GET` | `/v1/standing-instructions?customerId=` | `scheduleapi.read` | Paged list |
| `PUT` | `/v1/standing-instructions/{id}` | `scheduleapi.write` | **Real, in-place edit** — amount, schedule, priority, `AllowPartialTransfer` (matching Fineract's own real `update()` behavior, T-069 Finding 7, not a cancel-and-recreate workflow). Rejected on a `Deleted` instruction, matching Fineract's own confirmed rule exactly |
| `DELETE` | `/v1/standing-instructions/{id}` | `scheduleapi.write` | Soft delete — `Status` → `Deleted`. Not a row removal, matching Fineract's own real mechanism |
| `PATCH` | `/v1/standing-instructions/{id}/status` | `scheduleapi.write` | `Active` ↔ `Disabled` only (pause/resume without deleting) — kept as its own narrow endpoint rather than folded into the general `PUT`, since this is the one field change that needs no other validation |
| `GET` | `/v1/standing-instructions/{id}/runs` | `scheduleapi.read` | Run history, paged |

## 5. What this design explicitly does not do

- **No financing-module integration.** Every field/enum value that requires a loan is modeled for
  schema parity and rejected clearly at creation time. This design does not propose, design, or build
  any part of the financing module itself — that stays fully gated on D-28's Sharia scholar sign-off.
- **No `OrchestratorApi`, `IdentityApi`, or `LedgerApi` change.** `ScheduleApi` calls
  `OrchestratorApi`'s existing `POST /v1/transfers` for every run, and `LedgerApi`'s existing `GET
  /api/accounts/{accountNumber}/balance` for the one narrow `AllowPartialTransfer` case (3.2) — both
  already-shipped endpoints, no new contract needed on either side.
- **No fund-contention priority ordering.** `Priority` is stored (schema parity) but not enforced —
  T-069's own research could not confirm Fineract's own shipped code enforces it either; inventing an
  ordering guarantee neither system actually backs up would be worse than honestly deferring it.
- **No staff/ops-console screen.** `OpsApi`/`garbati-ops-console` gain no standing-instruction UI in
  this design — a future, separate task if wanted.
- **No chunked/bulk creation**, no cron-expression scheduling beyond the Days/Weeks/Months/Years +
  interval + on-day/on-month shape above.

## 6. Decisions log

| # | Decision | Options considered | Chosen | Why |
|---|---|---|---|---|
| 1 | v1 feature-parity target | Minimal (account-transfer only, no editing, no partial-transfer) vs. full Fineract-equivalent shape | **Full shape, honestly adapted** — every Fineract axis modeled; everything requiring a loan rejected clearly until financing exists | Owner's explicit call — the full shape costs nothing extra to model now and avoids a second redesign once financing exists; rejecting cleanly (not silently) at the gaps matches this project's own established discipline |
| 2 | Retry-on-failure | Auto-retry vs. record and leave for the next natural occurrence | **No auto-retry** | Matches real-world convention (T-011) and Fineract's own real, bug-fixed, shipped behavior exactly (T-069) — resolves `ARCHITECTURE.md` Section 9's P-3 question |
| 3 | Joint accounts | Must support unattended M-of-N approval vs. single-holder only for v1 | **Single-holder only**, not pre-validated — the existing `IdentityApi` authorization chain already denies a joint-account attempt cleanly on its first run | Inventing an unattended-approval mechanism for M-of-N accounts is a much bigger, separate design question; the existing chain already fails safely with no new code needed |
| 4 | `AllowPartialTransfer`'s balance source | New `OrchestratorApi` balance/quote endpoint vs. a narrow read-only `LedgerApi` exception vs. defer the feature | **Narrow `LedgerApi` exception**, mirroring `OpsApi`'s own existing funding-endpoint precedent | A balance enquiry is read-only, carries no authorization question, and `OrchestratorApi` has no equivalent to proxy through; a new endpoint there would duplicate what `LedgerApi` already exposes for exactly this purpose |
| 5 | Scheduling mechanism | A new job-scheduling dependency (Quartz.NET, Hangfire) vs. a plain `BackgroundService` | **Plain `BackgroundService`**, polling hourly | No new dependency; matches this project's "simplest sufficient tool" discipline; nothing about v1's scale needs a distributed job scheduler |
| 6 | Run-history status values | Mirror Fineract's own (unconfirmed) literal strings vs. define GARBATI's own closed set | **GARBATI's own closed enum**: `Success`/`Partial`/`Failed`/`Skipped` | T-069 could not confirm Fineract's own exact literal strings from source; guessing at an unverified external format is worse than a clean, self-documented set, plus `Skipped` (a GARBATI addition) has no Fineract equivalent needed |
| 7 | Month-day recurrence (day 29-31) | Naive date-truncating arithmetic vs. clamp to the target month's real length | **Clamp** | T-069 found Fineract shipped and later fixed exactly this bug in production (FINERACT-2914) — starting from the fixed behavior instead of rediscovering the bug |
| 8 | Create-time idempotency | Leave `POST /v1/standing-instructions` without a client-supplied idempotency key (this table's own original state) vs. add one, mirroring every other create endpoint on the platform | **Added** `IdempotencyKey`/`InitiatedBy`/`RequestHash`, scoped per calling client, matching `LedgerApi`'s `POST /api/accounts` and `Garbati.IdentityApi`'s `POST /v1/customers` exactly | **Found during T-071's build, not a reopened decision** — this table simply never had the field; every other platform create endpoint already follows this exact convention (client-supplied key, request-hash fingerprint, insert-then-catch-the-unique-constraint so a network retry replays rather than duplicates or errors), and `StandingInstruction` creation has no principled reason to be the one exception. See Appendix A for what T-071 actually checked in `LedgerApi`'s/`Garbati.IdentityApi`'s real source before copying the shape |

## 7. Open questions / explicitly deferred

1. **Exact scheduler poll interval** — proposed hourly (3.1); not load-tested, no real traffic exists
   yet to tune against.
2. **Whether `OpsApi` eventually gets a standing-instruction console screen** — not scoped here.
3. **Fund-contention priority ordering** — explicitly deferred (Section 5), pending either a real need
   or a confirmed answer from Fineract's own current source on how/whether it actually enforces this.
4. **The scheduler's due-instruction query has no paging/batching** (`StandingInstructionRunner.
   RunDueInstructionsAsync` loads every due `Active` instruction in one query, per tick) — fine at
   today's real scale (noted by QA during T-071's build), worth revisiting only once the number of
   standing instructions due on a single hour, on a single tick, is actually large enough for it to
   matter; not a problem this design has any evidence yet that it has.

## Appendix A. T-071 build notes (project skeleton, entities, endpoints, scheduler, both clients)

Built everything in this document's v1 scope in one task, on `feature/T-071-foundation`, mirroring
`Garbati.OrchestratorApi`'s scaffold throughout (project layout, `DownstreamServicesOptions`, the
`Clients/` split, API-key auth). The task file left real judgment calls, recorded here plus the smaller
decisions made along the way:

1. **`POST /v1/standing-instructions` had no idempotency key at all until a QA review of this same PR
   caught it (decision #8) — a real gap in this document's own Section 2.1 table, not a silent
   deviation from an existing decision.** The first pass of this build read Section 2.1 literally (no
   `IdempotencyKey` field was listed) and documented that reading explicitly in code and in this
   document's own text — but every other create endpoint on this platform (checked directly against
   `LedgerApi.Services.Implementations.AccountService.CreateAccountAsync` and `Garbati.IdentityApi.
   Services.Implementations.CustomerService.CreateCustomerAsync`, not assumed) follows an established,
   non-negotiable convention: a client-supplied key, scoped per calling client (`InitiatedBy`), a
   `RequestHash` fingerprint of the fields that define the created resource, and an insert-then-catch-
   the-unique-constraint flow so a network retry replays the original resource (same status code the
   sibling returns on replay - 200, not 201) instead of creating a duplicate or erroring. Fixed by adding
   `IdempotencyKey`/`InitiatedBy`/`RequestHash` to the entity (Section 2.1), a new migration, the same
   request-hash/retry-loop shape in `StandingInstructionService.CreateAsync`, and two regression tests
   (`Controllers/CreateStandingInstructionTests`: same-key-same-body replays with no second row; same-
   key-different-body returns 409). One real wrinkle this entity has that neither sibling's does: a
   second, independent uniqueness rule (`Name`) that a legitimate replay would otherwise always collide
   with if the idempotency match weren't checked FIRST, before the `Name` pre-check - documented in
   `StandingInstructionService.CreateAsync`'s own comment.
2. **A second QA pass on the same idempotency fix found the upfront check above was not enough: a real,
   deterministic (5/5) race.** Two requests sharing the IDENTICAL `IdempotencyKey` AND identical `Name`
   (the exact "client retried because the first response was lost" scenario) can both pass the upfront
   `(InitiatedBy, IdempotencyKey)` check and the `Name`-uniqueness pre-check before either has inserted -
   the loser's insert then violates BOTH unique constraints at once, and Postgres reports back only ONE
   constraint name, which can be `ix_standing_instructions_name`. The code unconditionally treated that
   as a genuine name conflict (409) without first checking whether it was actually this caller's own
   concurrent replay - told a real caller "duplicate name" instead of its own successful response, which
   a real caller would reasonably react to by retrying with a NEW key, recreating the exact duplicate-
   recurring-payment risk this whole feature exists to close. Fixed by re-checking for an idempotent
   match, inside BOTH places that could otherwise conclude "genuine name conflict" (the `AnyAsync`
   pre-check and the `ix_standing_instructions_name` catch branch), before throwing
   `DuplicateStandingInstructionNameException` - extracted into one shared `FindReplayAsync` helper so
   the two call sites cannot drift. Proven the way QA asked: a new `Task.WhenAll`-based concurrent test
   (`Create_WithTwoTrulyConcurrentRequests_SameKeyAndSameName_...`) failed 5/5 against the pre-fix code
   and passed 5/5 after.
3. **Two QA nits addressed in the same pass, not deferred:** `IdempotencyKey`/`InitiatedBy`/`RequestHash`
   are now required (`NOT NULL`) columns, not nullable - this is a brand-new service with zero existing
   rows, so the one reason `LedgerApi`'s/`Garbati.IdentityApi`'s own equivalent columns stay nullable
   (pre-existing seed data predating those columns) never applies here. And Section 7's open questions
   now note the scheduler's due-instruction query has no paging/batching, fine at today's scale.
4. **`RecurrenceOnMonth`/day-of-week numbering** — see Section 3.4's own inline note, added by this
   build: the task file's summary of the due-date check mentions a `RecurrenceOnMonth` field this
   document's own Section 2.1 table does not define: not invented. `RecurrenceOnDay` as a day of week
   (`Weeks`) uses ISO 8601 numbering (`1` = Monday … `7` = Sunday), undocumented in Section 2.1 and
   genuinely open until this build.
5. **`PUT`'s exact field set.** Section 4's own row says "amount, schedule, priority,
   `AllowPartialTransfer`" — read as `Amount`, `ValidFrom`/`ValidTill`/`RecurrenceType`/
   `RecurrenceFrequency`/`RecurrenceInterval`/`RecurrenceOnDay` ("schedule"), `Priority`,
   `AllowPartialTransfer`. `Name`/`CustomerId`/the four account fields/`TransferType`/`InstructionType`
   are deliberately NOT editable through `PUT` — changing the parties or kind of a standing instruction
   is a cancel-and-recreate workflow in this design, not an edit. `RecurrenceType` is re-validated against
   the same "only `Periodic`" rule `POST` enforces, since `PUT` is otherwise a way to smuggle an
   instruction into `AsPerDues` after creation.
6. **Idempotency key uses the internal numeric `Id`, confirmed as intentional, not an inconsistency.**
   Section 2.2 types `StandingInstructionRun.StandingInstructionId` as `long` (the FK), and Section 3.1's
   `sched:{standingInstructionId}:{scheduledDate:yyyy-MM-dd}` uses that same field name — read literally,
   this is the *internal* PK, not the public `StandingInstructionId` string. Checked against `LedgerApi`'s
   own real precedent before treating this as a platform-convention violation to silently "fix": `ChainService`
   generates leg keys as `"chain:{chain.Id}:{sequence}"` using `TransactionChain`'s internal PK, the exact
   same shape. An internally-generated, caller-unreachable dedup key already has established precedent as
   the one exception to this platform's "never expose the internal Id" discipline — implemented literally,
   not changed.
7. **No `AuditLog` entity.** `ARCHITECTURE.md` Section 5 names an audit convention every service follows,
   but this document's own Section 2 defines exactly two entities, and the task file's entity list is the
   same two — `StandingInstructionRun` already serves as the durable "what happened" record this service's
   own job is built around. Not added; flagged here rather than silently matching `OrchestratorApi`'s
   separate `AuditLogs` table it was not asked to build.
8. **`StandingInstructionId` prefix: `SI-`.** No existing convention to extend (this is the first entity
   needing one in this service); chosen for the obvious reason (Standing Instruction), same generation
   scheme as `OrchestratorApi`'s `OperationIdGenerator`.
9. **Duplicate `Name` → 409, run history ordering → most-recent-first.** Neither is specified by this
   document. 409 matches this platform's existing "same key/identity, different content" conflict
   convention; most-recent-first matches how a statement/run history reads in every other context on this
   platform that has one, as opposed to `Garbati.IdentityApi`'s oldest-first approvals *queue* (a queue is
   worked through in order; a run history is read, not worked).
10. **Testing against mocked HTTP clients, not real locally-run `OrchestratorApi`/`LedgerApi` instances** —
   the same judgment call `OrchestratorApi`'s own Appendix A made for its first build, for the same reason
   (standing up two more full services inside this task's CI run is a lot of added infrastructure for a
   first build). **What this does not prove:** that `OrchestratorApi`'s and `LedgerApi`'s *currently
   deployed* code still returns exactly the shapes this service's `Clients/Contracts/` mirror — only that
   this service's code is right about what their real source said on 10 November 2026. See the PR
   description for whether a manual smoke test against real local instances was run.

See the PR description for how this was tested, what was run and not run, and the full `LEARNING_LOG`
entry.
