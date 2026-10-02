# Parser Correctness Tests (Risk #2) Implementation Plan

## Overview

Rollout Phase 2 of `context/foundation/test-plan.md`. Add xUnit tests (no product code changes) proving that a CSV/PDF row yields exactly the date and amount printed in the source, regardless of the machine's culture, and pinning what each bank's integrity check does and does not catch. The plan challenges two beliefs from the test plan: "passes on the fixture so it parses correctly" and "the balance check catches every misread".

## Current State Analysis

Parsers pin their culture explicitly (Invariant for dates, `pl-PL` for amounts), so culture independence holds by construction but no test proves it. The balance check exists only for PDFs, and unevenly (mBank anchored; VeloBank and Erste internal-consistency only). CSV parsers have no integrity check. Existing tests assert authored literals, but PDF expectations flow through builders that share the parser's reading of the format, and one live test (mBank dmy CSV) asserts count only. Full detail: `research.md`.

## Desired End State

`dotnet test` (from `MyFinances/backend`) is green and includes tests that:
- assert dates and amounts by value for every CSV format variant, including mBank `dd.MM.yyyy`;
- prove locale-sensitive separators (thousands space, NBSP, U+2212, dot decimal) are either parsed correctly or rejected/skipped-and-counted, never mis-valued;
- characterize the known silent paths (mBank CSV early stop, VeloBank weak check) so a behavior change is visible;
- run key fixtures under a matrix of thread cultures and get identical literals.

### Key Discoveries:

- Every date/number parse passes an explicit culture; none uses `CurrentCulture` (`Import/MBankCsvParser.cs:155,167`, `Import/ErsteCsvParser.cs:137-138`, `Import/Pdf/PdfStatementReader.cs:27,133`).
- mBank CSV `break`s on the first unparseable date and does not count dropped rows (`Import/MBankCsvParser.cs:155-161`).
- PDF unreadable rows throw `StatementIntegrityException` → 422 (`Import/ImportEndpoints.cs:73-82`); CSV bad amounts are skipped and counted (`Import/MBankCsvParser.cs:171-176`).
- VeloBank check is pairwise on booked PLN rows and resets on foreign rows (`Import/VeloBankPdfParser.cs:299-344`); Erste's equivalent blind spots are already pinned (`Tests/Import/Parsers/ErstePdfParserTests.cs:450,463`) — follow that pattern.
- Count-only dmy test: `Tests/Import/Parsers/MBankCsvParserTests.cs:93-106`.
- Independent-oracle builders already exist: `Tests/Support/MBankCsvBuilder.cs`, `Tests/Support/MBankPairedStatement.cs`.
- Lesson in force (`context/foundation/lessons.md`): every test uses `// Arrange`, `// Act`, `// Assert` comments and shared per-class setup.

## What We're NOT Doing

- No product code changes: the mBank CSV early stop is pinned as `_KnownLimitation`, not fixed.
- No real redacted bank PDFs; PDF fixtures stay synthetic (the limitation is recorded, not solved).
- No `InvariantGlobalization` / no-ICU run configuration; ICU is covered only by a pl-PL separator guard test.
- No full pinning of every Erste/VeloBank blind spot beyond VeloBank's enumerated cases (Erste's already exist).
- The wrong-bank manual choice returning 200 with zero rows (`ImportEndpoints.cs:51-54`) is out of scope.
- No CSV balance check (FR-018 specifies PDFs only).

## Implementation Approach

Three phases, each independently verifiable by `dotnet test`: CSV, then PDF/integrity, then culture. Use authored literals from outside the builders as oracles. Where behavior is unknown (pl-PL with a regular space or NBSP in CSV `Kwota`), run it once, then pin the observed outcome, which must be "parsed to the correct value" or "skipped and counted", never a wrong value. Extend existing test classes and `Tests/Support` helpers; follow the existing authored-literal → builder → parser → literal-assert pattern.

## Phase 1: CSV parser correctness

### Overview

Close the CSV gaps: by-value dates, locale separators in `Kwota`, and the silent early stop.

### Changes Required:

#### 1. mBank CSV tests

**File**: `MyFinances/backend/Tests/Import/Parsers/MBankCsvParserTests.cs`

**Intent**: Replace the count-only dmy assertion with by-value date and amount assertions (a day/month swap must fail). Add cases for `Kwota` with a thousands space and with NBSP, pinning the empirically observed outcome. Add `MidFileBadDate_KnownLimitation`: a fixture with a valid row, a row dated like `1.8.2026`, then more valid rows, asserting the parse returns only the rows before it and `SkippedErrorCount` is zero.

**Contract**: Test method names carry the outcome (e.g. `..._KnownLimitation`); expected values are authored literals, not derived from parser output or builder format helpers. Use `MBankCsvBuilder` for file construction, cp1250 or ASCII-safe content (per Phase 1 dedup plan).

#### 2. Erste CSV tests

**File**: `MyFinances/backend/Tests/Import/Parsers/ErsteCsvParserTests.cs`

**Intent**: Where Erste variants (delimiter, EUR, bad amount) compare only counts or parser outputs against each other, add literal by-value date and amount assertions for at least one row per variant, including a thousands-separated amount.

**Contract**: No change to production code; reuse existing fixtures/builders.

### Success Criteria:

#### Automated Verification:

- Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- Build has no warnings introduced: `dotnet build` (from `MyFinances/backend`)
- mBank dmy test fails if day/month are swapped (mutation spot-check: temporarily swap in `MBankCsvParser.cs:155` formats and confirm the new test fails, then revert)

#### Manual Verification:

- New tests read in Arrange/Act/Assert form with a shared per-class setup
- Pinned outcomes for thousands-space and NBSP `Kwota` are recorded in the test names or comments and none mis-values the amount

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: PDF correctness and integrity oracles

### Overview

Break the builder-parser circularity at the cheapest layer, and make each bank's integrity guarantee explicit.

### Changes Required:

#### 1. Raw-text rejection and literal oracles

**File**: `MyFinances/backend/Tests/Import/Parsers/MBankPdfParserTests.cs`, `VeloBankPdfParserTests.cs`, `ErstePdfParserTests.cs`

**Intent**: Add tests where an amount cell carries U+2212 (minus), NBSP, or a dot decimal and the parser rejects the statement (throws `StatementIntegrityException`) rather than reading a wrong value. Add literal per-row date/amount assertions written as plain literals taken from the bank layout, not through `FormatMoney`/`FormatDate`, for at least one representative statement per bank.

**Contract**: Reuse the existing builders to produce PDFs but assert against authored literals; the rejection tests may need a builder option or a raw-text hook to inject the offending character without going through `FormatMoney`. Keep any builder change minimal and test-only.

#### 2. VeloBank blind spots and mBank compensating case

**File**: `MyFinances/backend/Tests/Import/Parsers/VeloBankPdfParserTests.cs`, `MBankPdfParserTests.cs`

**Intent**: Pin VeloBank's enumerated blind spots (dropped newest/oldest row accepted; a drop adjacent to a pending or foreign-currency row accepted) mirroring the Erste pins at `ErstePdfParserTests.cs:450,463`. Add one mBank test with a compensating error (a row amount and its running balance both altered consistently) to document what the anchored check catches (opening/closing/turnover anchors) versus what slips through.

**Contract**: Names state the behavior (e.g. `..._DroppedNewestRow_IsAccepted_KnownLimitation`); expected outcomes are literal.

#### 3. Endpoint leaves DB empty on 422

**File**: `MyFinances/backend/Tests/Import/ImportEndpointsTests.cs`

**Intent**: For one rejected PDF parse (e.g. tampered mBank closing balance), additionally assert via `GetStoredAsync` that no transactions were persisted.

**Contract**: Uses existing Phase 1 (dedup) helpers in `Tests/Support/ImportTestHelpers.cs`.

### Success Criteria:

#### Automated Verification:

- Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- Rejection tests fail when the parser's `NumberPattern` is loosened to accept U+2212/NBSP (mutation spot-check in `Import/Pdf/PdfStatementReader.cs:24`, then revert)

#### Manual Verification:

- Literal PDF oracles are written from the printed layout, not copied from builder output
- Characterization tests are clearly named as limitations and each says which behavior would justify updating it

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Culture independence

### Overview

Prove parsing does not depend on thread culture, and guard the pl-PL assumption.

### Changes Required:

#### 1. Culture-matrix helper

**File**: `MyFinances/backend/Tests/Support/CultureMatrix.cs` (new)

**Intent**: Provide a helper that runs an action under each culture in a fixed set (en-US, de-DE, tr-TR, pl-PL, Invariant), setting `CurrentCulture` and `CurrentUICulture`, and restoring both in a `finally`.

**Contract**: Synchronous action overload (parsers are synchronous); culture list is a single public constant so tests share it; results are asserted by the calling tests against literals, never against the first run's output.

#### 2. Culture tests per parser

**File**: `MyFinances/backend/Tests/Import/Parsers/CultureIndependenceTests.cs` (new)

**Intent**: For each of the five parsers, parse one key fixture under every matrix culture and assert the same authored date/amount literals (including a thousands-separated amount and a decimal comma). Add one guard test asserting `CultureInfo.GetCultureInfo("pl-PL").NumberFormat.NumberDecimalSeparator == ","` so an ICU-less runtime shows up as a named failure.

**Contract**: Follows the Arrange/Act/Assert lesson; mBank CSV tests must register `CodePagesEncodingProvider` as the existing mBank CSV tests do.

#### 3. Test-plan status

**File**: `context/foundation/test-plan.md`

**Intent**: Update the Phase 2 row status and note findings (CSV early-stop pinned, PDF fixtures still synthetic, ICU only guarded).

**Contract**: Edit only the Phase 2 row and the TBD note at line 112 about the culture pattern.

### Success Criteria:

#### Automated Verification:

- Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- Culture test fails if a parser is changed to use `CurrentCulture` (mutation spot-check on one `decimal.Parse` call in `Import/MBankCsvParser.cs:167`, then revert)
- Thread culture is restored after each test (the full suite passes in a single run with tests in any order)

#### Manual Verification:

- `test-plan.md` Phase 2 row reflects the outcome and remaining limitations

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- By-value date/amount assertions per CSV variant and per PDF bank.
- Rejection of non-ASCII or non-comma number text in PDFs; pinned outcomes for CSV `Kwota` separators.
- Characterization tests: mBank CSV early stop, VeloBank dropped/adjacent rows, mBank compensating error.
- Culture matrix across all five parsers plus the pl-PL separator guard.

### Integration Tests:

- One endpoint assertion that a 422 persists nothing.

### Manual Testing Steps:

1. Run the mutation spot-checks listed in each phase and confirm the targeted test fails, then revert the mutation.
2. Skim the new tests for literal oracles and A/A/A sections.

## Performance Considerations

The culture matrix multiplies parses by five for a few fixtures; negligible against current suite size. PDF fixture regeneration (`REGENERATE_PDF_FIXTURES=1`) must not be triggered by these tests.

## Migration Notes

None. Tests only.

## References

- Related research: `context/changes/testing-parser-correctness/research.md`
- Test plan: `context/foundation/test-plan.md` (Phase 2, Risk #2)
- Similar pinned-limitation pattern: `MyFinances/backend/Tests/Import/Parsers/ErstePdfParserTests.cs:450,463`
- Prior phase: `context/changes/testing-import-integrity-dedup/plan.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: CSV parser correctness

#### Automated

- [x] 1.1 Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- [x] 1.2 Build has no warnings introduced: `dotnet build` (from `MyFinances/backend`)
- [x] 1.3 mBank dmy test fails if day/month are swapped (mutation spot-check, then revert)

#### Manual

- [ ] 1.4 New tests read in Arrange/Act/Assert form with a shared per-class setup
- [ ] 1.5 Pinned outcomes for thousands-space and NBSP `Kwota` are recorded and none mis-values the amount

### Phase 2: PDF correctness and integrity oracles

#### Automated

- [ ] 2.1 Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- [ ] 2.2 Rejection tests fail when `NumberPattern` is loosened to accept U+2212/NBSP (mutation spot-check, then revert)

#### Manual

- [ ] 2.3 Literal PDF oracles are written from the printed layout, not copied from builder output
- [ ] 2.4 Characterization tests are clearly named as limitations and say which behavior would justify updating them

### Phase 3: Culture independence

#### Automated

- [ ] 3.1 Backend tests pass: `dotnet test` (from `MyFinances/backend`)
- [ ] 3.2 Culture test fails if a parser is changed to use `CurrentCulture` (mutation spot-check, then revert)
- [ ] 3.3 Thread culture is restored after each test (full suite passes in a single run in any order)

#### Manual

- [ ] 3.4 `test-plan.md` Phase 2 row reflects the outcome and remaining limitations
