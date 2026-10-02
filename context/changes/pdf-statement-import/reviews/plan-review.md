<!-- PLAN-REVIEW-REPORT -->
# Plan Review: PDF Statement Import (VeloBank)

- **Plan**: context/changes/pdf-statement-import/plan.md
- **Mode**: Deep
- **Date**: 2026-10-01
- **Verdict**: REVISE (SOUND after triage — all findings fixed in the plan)
- **Findings**: 0 critical, 2 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | PASS (2 observations) |

## Grounding

25/25 paths ✓ (incl. the 2 real sample PDFs outside the repo), 8/8 symbols ✓, brief↔plan ✓, Progress↔Phase ✓ (5/5 phases, 31/31 criteria). PdfPig 0.1.16 API surface verified against the restored package (net10.0 → net9.0 assets, no transitive dependencies, Apache-2.0). Blast radius: only the two CSV parsers implement `IBankStatementParser` (no test fakes), so adding `Format` breaks nothing; `Transaction.Description` is an unbounded `text` column.

## Findings

### F1 — Reject-all policy is undefined for foreign-currency card rows

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Implementation Approach → Integrity; Phase 4 → Parser contract
- **Detail**: Check (2) compares the "na kwotę" amount with the amount column for "PLN card rows"; check (1) runs over "consecutive booked PLN rows". Neither says what happens to a foreign-currency card payment (unverified, no sample). If the amount column is PLN but the description says EUR, check (2) throws and the whole file gets a 422. If the amount column is EUR, the row is skipped but the printed balance moved, so the chain breaks across it (the planned test "EUR row … checks still pass around it" passes only because the generator chooses the balance behaviour). The 422 named only the page, so one USD/EUR subscription charge would make the statement un-importable with no way to find the row. Other unverified row types (fee, refund, BLIK, a row split across a page break) end the same way.
- **Fix A ⭐ Recommended**: Currency-scoped card check + adjacency rule for the balance chain + actionable 422 title (check, page, row date)
  - Strength: Keeps all three guards and the user's reject decision for the balance chain; removes the foreign-currency false reject; a remaining false reject becomes diagnosable.
  - Tradeoff: Still all-or-nothing for other unverified row types.
  - Confidence: MED — depends on how VeloBank prints foreign rows, which no sample shows.
  - Blind spot: No foreign-currency sample.
- **Fix B**: Card-amount check on pending rows only, plus adjacency rule and diagnostics
  - Strength: Smallest false-reject surface.
  - Tradeoff: Loses a second corroboration on booked rows.
  - Confidence: MED — argued from the chain arithmetic.
  - Blind spot: Pending foreign-currency descriptions still need A's currency guard.
- **Decision**: FIXED (Fix A) — plan.md Integrity bullet, Phase 2 exception contract, Phase 4 integrity bullet and parser tests, plus the two matching plan-brief lines.

### F2 — "Never throws" and "not encrypted" don't match PdfPig's real behaviour

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 4 → Parser (CanParse/Parse); Performance Considerations
- **Detail**: Verified on PdfPig 0.1.16 / net10.0: `IsEncrypted` is true for owner-password-only files that read fine (a password-protected file fails with `PdfDocumentEncryptedException` from `Open`); `GetPage`/`GetWords` can throw `InvalidOperationException` / `InvalidFontFormatException` (`SkipMissingFonts` defaults to false), not only `Open`; and the 5 MB + 200-page limits do not bound memory (a ~1000:1 Flate stream decoded to 64 MB in 214 ms, 284 MB peak; PdfPig has no size limit).
- **Fix**: Drop the `IsEncrypted` condition; wrap open, page read and extraction in one catch-all inside CanParse (→ false) and Parse (→ empty result), letting only `StatementIntegrityException` escape; reword the Performance note.
- **Decision**: FIXED — plan.md Phase 4 CanParse and Parse bullets, Performance Considerations.

### F3 — Generator gap spec sits on PdfPig's word-splitting boundary

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Critical Implementation Details (last bullet); Phase 3 → Generator
- **Detail**: A gap of exactly one space width (0.278 em) still merged `1`+`014,84`; ≥1.05 spaces split; PdfPig's adaptive gap rule makes the outcome depend on the other text on the page. `DrawRectangle(fill:true)` also strokes (`IsStroked` stays true even at line width 0), unlike the real files. A string with real space characters (or NBSP) split correctly in every probe.
- **Fix**: Write each cell line as one string with real spaces (after confirming on a real-sample letter dump that its thousands separator is a space glyph), else gap ≥ 1.1 space widths; keep the split-balance fixture test; the parser keys on `IsFilled` + geometry, never `IsStroked`.
- **Decision**: FIXED — plan.md Key Discoveries, Critical Implementation Details, Phase 3 generator contract, Phase 4 parser bullet.

### F4 — Deferred obligations have no tracked step

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Manual Testing Steps #5; Phase 1 → criterion 1.5
- **Detail**: (a) The only guard for the accepted pending-row double-count risk (Manual Testing step 5, "a few days later") has no Progress item, and its wording covers only a changed description — a card hold that settles for a different amount double-counts too. (b) The repo has one GitHub issue per roadmap slice, including proposed ones (#6 S-05, #8 S-07), per lessons.md; criterion 1.5 verified an S-11 issue but nothing created it and S-12/S-13 got none.
- **Fix**: Phase 1 gains a "GitHub issues" change (S-11 `status: planning`, S-12/S-13 `status: proposed`, ask before creating) and criterion 1.5 covers all three; the post-merge pending-row check is recorded as an open follow-up in S-11's roadmap block and issue, broadened to "description or amount differs".
- **Decision**: FIXED — plan.md Phase 1 (roadmap contract, new section 3, criterion 1.5 in body and Progress), Manual Testing step 5, pending-row risk wording; plan-brief open risk.
