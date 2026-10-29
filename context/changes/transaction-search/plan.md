# Transaction Search Implementation Plan

## Overview

Add an optional `search` query parameter to `GET /api/transactions` so the user can find transactions by a fragment of the description.

## Current State Analysis

[TransactionEndpoints.cs](../../../MyFinances/backend/Transactions/TransactionEndpoints.cs) lists the current user's transactions with optional `categoryId`, period and `kind` filters, built with LINQ over `db.Transactions`. Tests live in `MyFinances/backend/Tests/Transactions/TransactionEndpointsTests.cs`.

## Desired End State

`GET /api/transactions?search=biedronka` returns only the current user's transactions whose description contains the text, case-insensitively. The filter combines with all existing filters and with paging.

## What We're NOT Doing

- No frontend changes (no search box in the UI).
- No full-text search, ranking or fuzzy matching.
- No changes to paging defaults (`DefaultTake`, `MaxTake`).

## Phase 1: Backend search filter

### Changes Required:

#### 1. `MyFinances/backend/Transactions/TransactionEndpoints.cs`

- Add `string? search` to the GET list handler.
- Reject a `search` longer than 100 characters with 400 Bad Request.
- Filter with LINQ / EF Core only (parameterized), never by concatenating user input into SQL.
- Keep the existing `UserId` scoping.

#### 2. `MyFinances/backend/Tests/Transactions/TransactionEndpointsTests.cs`

- Add tests: matches case-insensitively, does not return another user's transactions, `search` over 100 characters returns 400.

### Success Criteria:

#### Automated Verification:

- Backend builds: `dotnet build` (from `MyFinances/backend`)
- All backend tests pass: `dotnet test` (from `MyFinances/backend`)

#### Manual Verification:

- `GET /api/transactions?search=...` in Swagger UI returns only matching rows of the logged-in user.
