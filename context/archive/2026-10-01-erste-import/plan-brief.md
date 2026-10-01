# Erste Bank Polska CSV Import — Plan Brief

> Full plan: `context/changes/erste-import/plan.md`

## What & Why

Let the user import an Erste Bank Polska "Historia" CSV through the same import → dedup → categorize → chart loop already working for mBank, completing PLN coverage of the second target bank in FR-003. Real Erste exports are available, so the format risk is already retired.

## Starting Point

Import is built around a generic `IBankStatementParser`; only `MBankCsvParser` exists. Endpoints, dedup hash, account bank list and transfer detection already consume parsers generically, so only the parser, its registration and one frontend list entry are missing.

## Desired End State

Uploading an Erste export auto-detects "Erste", shows its 29 rows for review, flags duplicates on re-import, and committed rows flow into categorization, charts and transfer detection. "Erste" is selectable when creating accounts and in the manual bank picker.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Sequencing vs S-07 (Revolut) | Build S-08 now, waive the PRD gate | Real Erste samples exist and the parser is independent of Revolut; roadmap prerequisite updated. |
| Transaction Date | Transaction date (field 1), not booking date | Month-end purchases stay in the month they happened; still within the ±2 day transfer tolerance. |
| Non-PLN statement | Return no rows, count every data row as skipped | Matches PRD FR-003 (PLN-only, skipped count) without a new error path in the shared endpoint. |
| Description | Field 2 only, counterparty ignored | Simplest, mirrors mBank's single-column mapping. |
| Detection | Structure of the summary line (no header exists) | Erste exports have no header row; shape is distinctive and does not collide with mBank. |
| Delimiter | Support all four Erste options (semicolon, comma, tab, pipe), detected by structural check of line 1 | Layout is identical across variants; character counting fails because `;` files contain decimal commas. |
| Test data | Redacted fixture per delimiter variant, same shape as real files | Samples contain real IBANs, name and address. |

## Scope

**In scope:** `ErsteCsvParser`, redacted fixture + parser tests, DI registration, `SUPPORTED_BANKS` entry, endpoint tests, roadmap prerequisite update.

**Out of scope:** Revolut (S-07), account-number or balance verification, counterparty columns, round-up ("Wpłata końcówek") transfer recognition, any endpoint/schema/hash change.

## Architecture / Approach

A new class mirroring `MBankCsvParser` (CsvHelper, no header, pl-PL decimals, row-level error counting) that first detects which of the four delimiters the file uses. Line 1 is a statement summary used for detection and currency; data rows map field 1 → date, 2 → description, 5 → amount. Registered with one `AddScoped` line, after which existing endpoints pick it up.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Parser, fixture, unit tests | `ErsteCsvParser` proven against a redacted real-format file | Fixture accidentally keeping real personal data |
| 2. Wiring and roadmap | DI + frontend list + endpoint tests + roadmap update | Detection false positive/negative across parsers |

**Prerequisites:** S-01 (done). None for S-07.
**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- Assumes all Erste exports share the sample's shape (summary line first, no header); other export variants would fail detection and need the manual bank picker plus a new format.
- A description containing the chosen delimiter unquoted would split wrongly; the samples show no such case (the comma variant quotes amounts), so it is assumed Erste quotes such fields.
- Three identical rows in one file import together on first import (dedup compares to the DB only) — same behaviour as mBank.
- Transaction date vs. mBank's booking date means the same transfer can differ by a few days across banks.

## Success Criteria (Summary)

- Importing the redacted Erste samples in all four delimiter variants yields correct rows (transaction dates, signed amounts), and a re-import flags them all as duplicates.
- Full backend test suite and frontend typecheck/build pass.
- Roadmap no longer gates S-08 on S-07.
