# Parser Correctness Tests (Risk #2) — Plan Brief

> Full plan: `context/changes/testing-parser-correctness/plan.md`
> Research: `context/changes/testing-parser-correctness/research.md`

## What & Why

Add tests (no product changes) proving that a CSV/PDF row yields exactly the date and amount printed in the source, whatever the machine's culture, and that each bank's integrity guarantee is explicit. The test plan's beliefs "passes on the fixture so it parses correctly" and "the balance check catches every misread" are both partly false per research.

## Starting Point

Parsers pin cultures explicitly, but no test proves culture independence. The balance check covers PDFs only, unevenly (mBank anchored; VeloBank and Erste internal-consistency only). Existing PDF fixtures are synthetic and built by builders that share the parser's format reading, and one mBank dmy test asserts count only.

## Desired End State

`dotnet test` is green with by-value date/amount assertions for every CSV variant, rejection tests for non-ASCII/dot-decimal PDF numbers, pinned characterizations of the known silent paths, and a culture matrix across all five parsers that gets identical literals.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| mBank CSV early stop on bad mid-file date | Pin as `_KnownLimitation`, do not fix | Keeps the rollout tests-only; fixing is a product policy change | Plan |
| Weak PDF checks | Pin VeloBank blind spots (Erste already pinned) + one mBank compensating-error test | Makes each bank's guarantee explicit without pinning every case | Plan |
| Culture proof | Matrix (en-US, de-DE, tr-TR, pl-PL, Invariant) + pl-PL separator guard | Proves thread-culture claim and surfaces an ICU-less runtime; no second run config | Plan |
| Independent oracles | Literal per-row assertions + raw-text rejection (U+2212, NBSP, dot) + CSV separator probes; no real PDFs | Cheapest layer that breaks builder-parser circularity | Plan |
| Unknown pl-PL space/NBSP behavior in CSV | Run once, pin observed outcome (correct value or skipped-and-counted) | Research Open Question 1; never mis-valued is the invariant | Research |
| Endpoint DB state after 422 | One assertion via `GetStoredAsync` | Research Open Question 5; cheap with existing helpers | Research |
| Test style | Arrange/Act/Assert comments, shared per-class setup | Accepted lesson | Lessons |

## Scope

**In scope:** CSV by-value tests and separator probes; PDF rejection and literal oracles; VeloBank and mBank characterizations; culture matrix helper and tests; test-plan status update.

**Out of scope:** Product code changes; real redacted PDFs; no-ICU run configuration; full pinning of every blind spot; wrong-bank manual choice behavior; CSV balance check.

## Architecture / Approach

Extend existing test classes and `Tests/Support`, following authored-literal rows → builder → parser → literal assertion. A new `CultureMatrix` helper sets and restores `CurrentCulture`/`CurrentUICulture` around synchronous parses. Each phase ends with a mutation spot-check proving the new tests can fail.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. CSV correctness | By-value mBank/Erste assertions, separator probes, early-stop pin | pl-PL outcome for space/NBSP unknown until run |
| 2. PDF and integrity oracles | Raw-text rejection, literal oracles, VeloBank/mBank characterizations, 422-leaves-DB-empty | Injecting offending characters may need a small builder hook |
| 3. Culture independence | `CultureMatrix`, per-parser culture tests, pl-PL guard, test-plan update | Culture mutation leaking across tests if not restored |

**Prerequisites:** Phase 1 (dedup) helpers already merged; `dotnet test` runs green today.
**Estimated effort:** ~2-3 sessions across 3 phases.

## Open Risks & Assumptions

- PDF fixtures stay synthetic, so a wrong reading of a real bank layout is not caught; this is recorded, not solved.
- An ICU-less production runtime is only flagged by the pl-PL guard, not exercised.
- Characterization tests will need updating if the early stop or weak checks are later strengthened.

## Success Criteria (Summary)

- A day/month swap, a loosened `NumberPattern`, or a parser switched to `CurrentCulture` each make a new test fail.
- The known silent paths (mBank CSV early stop, VeloBank weak checks) are visible as named tests.
- `test-plan.md` Phase 2 reflects the outcome and remaining limitations.
