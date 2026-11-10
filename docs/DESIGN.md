# Garbati.ScheduleApi — Design Document (v1)

**Status:** Design v1 — ratified, not yet built.
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

## 7. Open questions / explicitly deferred

1. **Exact scheduler poll interval** — proposed hourly (3.1); not load-tested, no real traffic exists
   yet to tune against.
2. **Whether `OpsApi` eventually gets a standing-instruction console screen** — not scoped here.
3. **Fund-contention priority ordering** — explicitly deferred (Section 5), pending either a real need
   or a confirmed answer from Fineract's own current source on how/whether it actually enforces this.
