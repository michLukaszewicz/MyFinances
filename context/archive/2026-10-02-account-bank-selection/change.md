---
change_id: account-bank-selection
title: Account bank dropdown (Polish banks + Other) for the import bank-mismatch check
status: archived
created: 2026-10-02
updated: 2026-10-02
archived_at: 2026-10-02T07:29:31Z
---

## Notes

Found while testing PDF import: an account saved with a free-text bank typo ("velobandk") made the import show a bank-mismatch warning even though the file was correctly detected as VeloBank. Account.BankName is free text and is both the user's label and the value the mismatch check compares.

Decision (user, 2026-10-02): keep BankName as the user's free-form account name and add a separate Bank field chosen from a dropdown of banks operating in Poland plus "Other". The mismatch check compares the detected parser's bank with Bank only; accounts with Bank = "Other" are never checked. Existing accounts are backfilled (case-insensitive match to the list, else "Other").

Bank list sources (web, 2026-10-02): porownywarkabankow.pl/banki, informacjakredytowa.com/banki.
