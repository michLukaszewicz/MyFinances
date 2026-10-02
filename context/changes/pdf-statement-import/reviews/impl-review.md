<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: PDF Statement Import (VeloBank)

- **Plan**: context/changes/pdf-statement-import/plan.md
- **Scope**: Full plan (Phases 1-5; Phase 5 reviewed from the staged, uncommitted state)
- **Reviewed phases**: 1, 2, 3, 4, 5
- **Date**: 2026-10-01
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Success Criteria: all automated checks passed in this session (dotnet test 250/250, npm run typecheck, npm run build). Manual items 5.4-5.8 are still pending, as expected before the Phase 5 commit.

## Findings

### F1 — Oversized number in a PDF cell causes a 500 instead of a 422

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Import/VeloBankPdfParser.cs:388 (`ParseNumber`, reached from `ReadRow` and `CheckCardAmount`)
- **Detail**: `MoneyPattern` and `CardAmountPattern` accept unbounded digit runs. A cell such as 29 digits followed by ",99 PLN" makes `decimal.Parse` throw `OverflowException`. Row reading and integrity checks run outside the PdfPig catch-all and the endpoint only maps `StatementIntegrityException`, so the user gets a 500.
- **Fix**: Bound the digit groups in both regexes (e.g. `{1,15}`) or use `decimal.TryParse` and reject with `UnreadableRow`; add a parser test with an oversized amount.
- **Decision**: FIXED (regex digit runs bounded to 16 digits; tests Parse_Rejects_AnAbsurdlyLargeAmountCell_AsUnreadable added)

### F2 — A later page with no readable header silently contributes zero rows

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Import/VeloBankPdfParser.cs:189-190 and 257-282 (`ReadRows` / `FindBands`)
- **Detail**: Only page 1 is checked for recognition. If a later page's header cells do not match (layout change, differently rendered page), `FindBands` returns no bands and that page's transactions are dropped without any signal. The balance chain compares consecutive rows only, so a missing page between two others would make the chain fail, but a missing last page or a missing first block of a page goes undetected. The result is a seemingly successful import with missing transactions, which the plan's integrity policy is meant to prevent.
- **Fix**: Reject with the `UnreadableRow` check (naming the page) when any page of a recognised statement yields no bands, since the real files repeat the header on every page.
  - Strength: Matches the plan's rule that unreadable parts of a recognised table are an integrity failure; verified real files carry a header block on every page.
  - Tradeoff: A genuinely blank trailing page would be rejected unless it is excluded by requiring words beyond the footer; check the real 90-day and one-year files still parse.
  - Confidence: MED — header-on-every-page is measured on two real samples only.
  - Blind spot: Behaviour on a statement with an empty last page or no transactions in the period.
- **Decision**: FIXED (later pages without a header block are rejected as unreadable, naming the page; generator option OmitHeaderOnPage and 2 tests added; real 90-day and one-year files re-checked: 17 and 62 rows, no integrity exception)

### F3 — Over-limit or unreadable PDFs give misleading feedback

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Import/VeloBankPdfParser.cs:105-125, 138
- **Detail**: `TryRead` collapses every failure (corrupt, encrypted, over 200 pages, even `OutOfMemoryException`) into "unreadable". Auto-detect then shows "could not recognize", and a manual `bank=VeloBank` shows 0 rows with HTTP 200. This is the plan's accepted manual-fallback contract and matches the CSV parsers, so not a regression.
- **Fix**: Optionally exclude fatal exceptions such as `OutOfMemoryException` from the catch-all; leave the rest as planned.
- **Decision**: SKIPPED

### F4 — Memory copies and per-band scans (accepted performance trade-offs)

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: MyFinances/backend/Import/ImportEndpoints.cs:35-37, MyFinances/backend/Import/VeloBankPdfParser.cs:110-112, 246-251, 284-294
- **Detail**: A 5 MB upload is copied several times, `CanParse` and `Parse` each open the PDF and extract page 1, parsing is synchronous, and word/rectangle scans are O(bands × words). All bounded by the 5 MB upload cap, the 200-page cap and the allow-listed single user, which the plan explicitly accepts.
- **Fix**: No change needed now; revisit if uploads are ever opened to other users.
- **Decision**: SKIPPED

### F5 — PDF magic bytes defined twice

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: MyFinances/backend/Import/StatementFormatSniffer.cs:12 and MyFinances/backend/Import/VeloBankPdfParser.cs:54
- **Detail**: Both files declare `"%PDF-"u8`. The parser's re-check is defensible because `Parse` can run on a manually chosen bank without the sniffer.
- **Fix**: Optionally expose one shared constant.
- **Decision**: SKIPPED

## Plan adherence summary

All planned items in Phases 1-5 are implemented as described. Known, accepted deviations: fixtures keep date/amount/balance top-aligned (matches the measured real files; centred layout is a test option), the extra `Support/VeloBankSampleData.cs`, extra tests, stricter parser rejection of unreadable balance/booking-date cells, and the card-amount check also running on pending rows. No scope-guardrail violations (no changes to `DedupHash`, `ParseResult`, commit endpoint, schema, Revolut, or mBank/Erste PDF parsers). No real personal data found in added files.
