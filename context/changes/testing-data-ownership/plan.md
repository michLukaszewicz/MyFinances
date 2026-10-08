# Data Ownership Tests (Risk #3) Implementation Plan

## Overview

Rollout Phase 3 of `context/foundation/test-plan.md`. Add xUnit integration tests (no product code changes) proving that a second real, authenticated user gets 404/400 and causes no state change on every read, edit, delete and categorize attempt against user A's transactions, accounts and imports, and add a structural guard that fails when a new `/api` endpoint is neither covered nor classified. Challenges two beliefs from the test plan: "logged in means authorized" and "every query already filters by owner".

## Current State Analysis

Owner scope is applied by hand in each endpoint (`research.md`). Existing ownership tests use a synthetic-Guid "other user" and mostly assert status codes only. No test uses two real sessions, re-reads A's data after a denied attempt, or notices a newly added unprotected endpoint.

## Desired End State

`dotnet test` (from `MyFinances/backend`) is green and includes `Tests/Ownership/*` which:
- run two real users (A, B) with separate cookie sessions against one host;
- assert, for every resource-id endpoint and for body/form-carried account ids, that B's attempt on A's data is denied (404 or 400) AND A's accounts, transactions and import batches are unchanged afterwards;
- assert B's reads (lists, queue, handled, dashboards, trend) contain none of A's data and B's aggregates equal B's own authored sums;
- assert cross-user dedup and transfer detection do not leak or pair across users;
- fail when an `/api` endpoint appears that is not classified as covered or global.

## What We're NOT Doing

- No product code changes (no global query filter, no refactor); gaps found are reported, not fixed.
- No browser/E2E tests, no real Postgres (InMemory reproduces a missing `Where`).
- No registration/login UI, no password/session-fixation testing (§7 of the test plan).
- No ownership test for categories: they are global by design.

## Implementation Approach

Three phases: harness, behavior tests, structural guard. Expected values are authored literals (account numbers, amounts, descriptions), never read back from the code under test. The state-unchanged assertion compares a before/after snapshot of A's rows read from the database.

## Phase 1: Two-user harness

### Overview

A reusable way to get two real sessions and to snapshot a user's data.

### Changes Required:

#### 1. Ownership test support

**File**: `MyFinances/backend/Tests/Support/TwoUserHarness.cs`

**Intent**: Given an `AuthApiFactory`, create user A via the existing register flow and user B via `UserManager<AppUser>` (registration is single-email), log B in over `/api/auth/login` on a separate client, and expose both clients, both user ids, and helpers to seed accounts/transactions for a user and to snapshot a user's accounts, transactions and import batches from the database as comparable values.

**Contract**: Test-only. A snapshot is an ordered list of field tuples (all persisted fields of the three entities for that `UserId`), compared with `Assert.Equal`.

### Success Criteria:

#### Automated Verification:

- Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- A smoke test proves `GET /api/auth/me` returns different emails for client A and client B in the same host.

#### Manual Verification:

- Harness has no production code changes.

---

## Phase 2: Cross-user denial and isolation tests

### Overview

Prove protection per the risk guidance, one scenario per endpoint family.

### Changes Required:

#### 1. Write/denial tests

**File**: `MyFinances/backend/Tests/Ownership/DataOwnershipWriteTests.cs`

**Intent**: With A owning an account, a categorized transaction and an import batch, and B owning their own account: B attempts `PUT`/`DELETE /api/accounts/{A}`, `PUT`/`DELETE /api/transactions/{A}`, `PUT /api/categorization/transactions/{A}` (category and transfer flag), `POST /api/transactions` and `PUT /api/transactions/{B's own}` with A's `accountId`, `POST /api/import/parse` and `/commit` with A's `accountId`. Each asserts the exact denial status and that A's snapshot is unchanged and B's own data holds only what B created.

**Contract**: `IAsyncLifetime` per class creates factory, harness and A's standard data; Arrange holds only what differs.

#### 2. Read/isolation tests

**File**: `MyFinances/backend/Tests/Ownership/DataOwnershipReadTests.cs`

**Intent**: B reads `/api/transactions` (also filtered by A's category/period), `/api/accounts/`, queue, handled, `/api/dashboard/category-spend`, `/category-income`, `/category-trend` and sees none of A's ids/amounts; B's aggregates equal authored sums of B's own rows. B importing a row identical to A's is not flagged duplicate; an A/B opposite-amount pair is not flagged as an internal transfer for either.

**Contract**: Authored literals for all expected amounts.

### Success Criteria:

#### Automated Verification:

- Backend tests pass: `dotnet test`
- Mutation spot-checks: removing the `UserId` condition from the PUT transaction lookup, the account DELETE lookup, and the dashboard query each makes a new test fail; each reverted afterwards.

#### Manual Verification:

- Denial tests assert state, not only status code.

---

## Phase 3: Endpoint inventory guard and cookbook

### Overview

Make "new endpoint, no ownership coverage" fail the build, and document the pattern.

### Changes Required:

#### 1. Route inventory guard

**File**: `MyFinances/backend/Tests/Ownership/EndpointOwnershipInventoryTests.cs`

**Intent**: Read `EndpointDataSource` from the factory host, list every `/api` endpoint (method + route pattern), and assert (a) each requires authorization unless explicitly allow-listed as anonymous (register, login, antiforgery-token), and (b) each is either in an explicit "ownership-tested" set (matching Phase 2 scenarios) or an explicit "global, no user data" set (`accounts/banks`, `categorization/categories`, `auth/me`, `auth/logout`). An unclassified endpoint fails the test with its name. Also assert an anonymous request to each endpoint group returns 401.

**Contract**: The classification lists are authored by hand; the failure message tells the next developer to add an ownership test.

#### 2. Cookbook

**File**: `context/foundation/test-plan.md`

**Intent**: Fill §6.3 (two-user pattern, snapshot rule, inventory guard), add the Phase 3 note to §6.4, set the §3 row to `complete` with limitations.

### Success Criteria:

#### Automated Verification:

- Backend tests pass: `dotnet test`
- Guard fails when a dummy unclassified endpoint is mapped (temporary mutation in `Program.cs`, then reverted).
- Build has no new warnings: `dotnet build`

#### Manual Verification:

- `test-plan.md` §6.3, §6.4 and the §3 row match what shipped.

---

## Testing Strategy

Integration tests over the real endpoints with two sessions; one structural test over the route table. No unit tests: the failure is a missing filter, visible only through the endpoint.

## Performance Considerations

Each class creates one in-memory host; negligible.

## Migration Notes

None. Tests only.

## References

- Research: `context/changes/testing-data-ownership/research.md`
- Test plan: `context/foundation/test-plan.md` (Phase 3, Risk #3)
- Prior pattern: `MyFinances/backend/Tests/Import/ImportDedupIntegrityTests.cs`, `Tests/Support/ImportTestHelpers.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Two-user harness

#### Automated

- [x] 1.1 Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- [x] 1.2 Smoke test shows two different emails for client A and client B in one host

#### Manual

- [x] 1.3 Harness has no production code changes

### Phase 2: Cross-user denial and isolation tests

#### Automated

- [x] 2.1 Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- [x] 2.2 Mutation spot-checks (transaction PUT lookup, account DELETE lookup, dashboard query) make a new test fail, then revert

#### Manual

- [x] 2.3 Denial tests assert state, not only status code

### Phase 3: Endpoint inventory guard and cookbook

#### Automated

- [x] 3.1 Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- [x] 3.2 Guard fails when a dummy unclassified endpoint is mapped (mutation, then revert)
- [x] 3.3 Build has no new warnings: `dotnet build` (from `MyFinances/backend`)

#### Manual

- [x] 3.4 `test-plan.md` §6.3, §6.4 and the §3 row match what shipped
