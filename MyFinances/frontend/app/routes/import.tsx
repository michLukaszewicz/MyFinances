import { useEffect, useState } from "react";
import { redirect } from "react-router";
import { apiFetch, ApiError } from "../lib/api";
import { notifyUncategorizedChanged } from "../lib/uncategorized";
import { AppHeader } from "../components/AppHeader";
import { Button } from "~/components/ui/button";
import { Card } from "~/components/ui/card";
import { Input, selectClassName } from "~/components/ui/input";

export async function clientLoader() {
  const res = await fetch("/api/auth/me", { credentials: "include" });
  if (!res.ok) throw redirect("/login");
  return null;
}

// Mirrors the backend's AccountContracts.cs AccountDto (see settings.tsx).
interface AccountDto {
  id: string;
  bankName: string;
  accountNumber: string;
  bank: string;
}

// Mirrors the backend's ImportContracts.cs (ImportParseResponse / ImportParseRow /
// ExistingTransactionDto). Dates are ISO strings on the wire (DateOnly -> "yyyy-MM-dd").
interface ExistingTransactionDto {
  date: string;
  description: string;
  amount: number;
}

interface ImportParseRow {
  date: string;
  description: string;
  amount: number;
  isDuplicate: boolean;
  existingTransaction: ExistingTransactionDto | null;
}

// bankMismatch: true when the detected/selected parser's bank differs from the chosen
// account's bank — non-blocking, the caller decides whether to proceed anyway.
// format: the parser's statement format (backend StatementFormat, string-serialized), echoed
// back as SourceFormat on commit. mixedFormatOverlapCount: transactions on the account in the
// file's date range that were imported from the other format — non-blocking warning.
type StatementFormatValue = "Csv" | "Pdf";

interface ImportParseResponse {
  bank: string;
  bankMismatch: boolean;
  rows: ImportParseRow[];
  skippedErrorCount: number;
  format: StatementFormatValue;
  mixedFormatOverlapCount: number;
}

interface ImportSummaryDto {
  importBatchId: string;
  accountId: string;
  importedAtUtc: string;
  importedCount: number;
  skippedDuplicateCount: number;
  skippedErrorCount: number;
}

// mBank, Erste and VeloBank are supported today — extend this list as more parsers ship.
const SUPPORTED_BANKS = ["mBank", "Erste", "VeloBank"];

// Mirrors the backend's RowDecision enum (ImportContracts.cs) — the value Phase 6
// will send per duplicate row in the /import/commit request.
type RowDecisionValue = "Keep" | "Skip";

function formatAmount(amount: number): string {
  return amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

async function extractErrorMessage(error: unknown): Promise<string> {
  if (error instanceof ApiError) {
    try {
      const body = await error.response.json();
      if (typeof body?.title === "string") {
        return body.title;
      }
    } catch {
      // fall through to generic message
    }
  }
  return "Something went wrong. Please try again.";
}

export default function Import() {
  const [accounts, setAccounts] = useState<AccountDto[]>([]);
  const [accountsLoading, setAccountsLoading] = useState(true);
  const [accountId, setAccountId] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [bank, setBank] = useState("");
  const [showBankPicker, setShowBankPicker] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<ImportParseResponse | null>(null);
  // Captured at submit time (not derivable from `result`, which only carries the
  // detected bank name) so the bank-mismatch banner can name the account that was
  // actually selected.
  const [selectedAccountBankName, setSelectedAccountBankName] = useState("");
  // Keyed by row index into result.rows — only duplicate rows ever get an entry;
  // non-duplicate rows are implicitly "keep" and never need a decision.
  const [decisions, setDecisions] = useState<Map<number, RowDecisionValue>>(new Map());
  const [committing, setCommitting] = useState(false);
  const [commitError, setCommitError] = useState<string | null>(null);
  const [summary, setSummary] = useState<ImportSummaryDto | null>(null);

  useEffect(() => {
    async function loadAccounts() {
      const list = await apiFetch<AccountDto[]>("/accounts/");
      setAccounts(list);
      setAccountsLoading(false);
    }
    void loadAccounts();
  }, []);

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!file || !accountId) return;

    setError(null);
    setSubmitting(true);
    try {
      const formData = new FormData();
      formData.append("file", file);
      formData.append("accountId", accountId);
      if (showBankPicker && bank) {
        formData.append("bank", bank);
      }

      // apiFetch does not force a Content-Type header, so the browser sets the
      // multipart boundary itself — do not set Content-Type here.
      const response = await apiFetch<ImportParseResponse>("/import/parse", {
        method: "POST",
        body: formData,
      });
      setSelectedAccountBankName(accounts.find((a) => a.id === accountId)?.bank ?? "");
      setResult(response);
      setDecisions(new Map());
    } catch (err) {
      // /import/parse returns 400 when the file is not recognised or the chosen bank has no
      // parser for its format — the only case where the picker helps. 422 (a recognised
      // statement that failed its integrity checks) shows its message without the picker, and
      // any other failure (network, 401, 500) shouldn't reveal it either, since retrying with a
      // bank won't fix those.
      if (err instanceof ApiError && err.response.status === 400) {
        setShowBankPicker(true);
      }
      setError(await extractErrorMessage(err));
    } finally {
      setSubmitting(false);
    }
  }

  function setRowDecision(index: number, decision: RowDecisionValue) {
    setDecisions((prev) => {
      const next = new Map(prev);
      next.set(index, decision);
      return next;
    });
  }

  const duplicateIndexes =
    result?.rows.reduce<number[]>((acc, row, index) => {
      if (row.isDuplicate) acc.push(index);
      return acc;
    }, []) ?? [];
  const allDuplicatesDecided = duplicateIndexes.every((index) => decisions.has(index));
  const allRowsAreDuplicates = duplicateIndexes.length > 0 && duplicateIndexes.length === (result?.rows.length ?? 0);

  async function commitImport(decisionsToUse: Map<number, RowDecisionValue>) {
    if (!result) return;

    setCommitError(null);
    setCommitting(true);
    try {
      const rows = result.rows.map((row, index) => ({
        Date: row.date,
        Description: row.description,
        Amount: row.amount,
        // Non-duplicate rows never get a decisions entry — they're implicitly kept.
        Decision: decisionsToUse.get(index) ?? "Keep",
      }));

      const response = await apiFetch<ImportSummaryDto>("/import/commit", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          AccountId: accountId,
          SkippedErrorCount: result.skippedErrorCount,
          Rows: rows,
          SourceFormat: result.format,
        }),
      });
      setSummary(response);
      notifyUncategorizedChanged();
    } catch (err) {
      setCommitError(await extractErrorMessage(err));
    } finally {
      setCommitting(false);
    }
  }

  function handleContinue() {
    void commitImport(decisions);
  }

  // Quick-exit for the common "I uploaded the same file by accident" case: skip every
  // duplicate row and commit immediately, instead of making the user also click Continue.
  function handleSkipAllDuplicates() {
    const allSkipped = new Map(decisions);
    for (const index of duplicateIndexes) {
      allSkipped.set(index, "Skip");
    }
    setDecisions(allSkipped);
    void commitImport(allSkipped);
  }

  return (
    <>
      <AppHeader authenticated />
      <main className="flex items-center justify-center pt-16 pb-4">
        <div className={`w-full space-y-6 px-4 ${result ? "max-w-2xl" : "max-w-[300px]"}`}>
          <h1
            className="text-center text-lg font-semibold text-foreground animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "0ms" }}
          >
            Import statement
          </h1>

          {summary ? (
            <div
              className="space-y-4 animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "50ms" }}
            >
              <Card className="gap-1 p-3 text-sm text-foreground">
                <p>
                  Imported <strong>{summary.importedCount}</strong> row(s) into{" "}
                  <strong>
                    {accounts.find((a) => a.id === summary.accountId)?.bankName ??
                      selectedAccountBankName}
                  </strong>
                  .
                </p>
                {summary.skippedDuplicateCount > 0 && (
                  <p className="text-muted-foreground">
                    {summary.skippedDuplicateCount} duplicate row(s) skipped.
                  </p>
                )}
                {summary.skippedErrorCount > 0 && (
                  <p className="text-muted-foreground">
                    {summary.skippedErrorCount} row(s) skipped due to parse errors.
                  </p>
                )}
              </Card>
            </div>
          ) : result ? (
            <div
              className="space-y-4 animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "50ms" }}
            >
              {result.bankMismatch && (
                <div className="space-y-2 rounded-lg border border-warning/60 bg-warning/10 p-3 text-sm text-warning">
                  <p>
                    This file looks like a <strong>{result.bank}</strong> export, but the selected
                    account is <strong>{selectedAccountBankName}</strong>. You can still continue
                    if this is correct.
                  </p>
                </div>
              )}

              {result.mixedFormatOverlapCount > 0 && (
                <div className="space-y-2 rounded-lg border border-warning/60 bg-warning/10 p-3 text-sm text-warning">
                  <p>
                    <strong>{result.mixedFormatOverlapCount}</strong> transaction(s) in this period
                    on this account were imported from{" "}
                    {result.format === "Pdf" ? "a CSV" : "a PDF"} file. Descriptions and dates
                    differ between CSV and PDF statements, so duplicates between them will not be
                    detected. You can still continue.
                  </p>
                </div>
              )}

              {allRowsAreDuplicates && (
                <div className="space-y-2 rounded-lg border border-warning/60 bg-warning/10 p-3 text-sm text-warning">
                  <p>
                    Every transaction in this file is already in your account — looks like it may
                    have been imported before. You can still choose "Keep" below for any row you
                    want to add anyway.
                  </p>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={handleSkipAllDuplicates}
                    disabled={committing}
                    className="border-warning/60 text-warning hover:bg-warning/10 hover:text-warning"
                  >
                    {committing ? "Skipping…" : "Skip all — none of these were new"}
                  </Button>
                </div>
              )}

              <Card className="gap-1 p-3 text-sm text-foreground">
                <p>
                  Parsed <strong>{result.rows.length}</strong> row(s) from{" "}
                  <strong>{result.bank}</strong>.
                </p>
                {result.skippedErrorCount > 0 && (
                  <p className="text-muted-foreground">
                    {result.skippedErrorCount} row(s) skipped due to parse errors.
                  </p>
                )}
                {duplicateIndexes.length > 0 && (
                  <p className="text-muted-foreground">
                    {duplicateIndexes.length} possible duplicate(s) need a decision
                    before you can continue.
                  </p>
                )}
              </Card>

              <ul className="space-y-2">
                {result.rows.map((row, index) => (
                  <li key={index}>
                    <Card
                      className={`gap-0 p-3 text-sm ${
                        row.isDuplicate ? "border-warning/60 bg-warning/10" : ""
                      }`}
                    >
                    {row.isDuplicate && row.existingTransaction ? (
                      <div className="space-y-2">
                        <p className="text-xs font-medium uppercase tracking-wide text-warning">
                          Possible duplicate
                        </p>
                        <div className="grid grid-cols-2 gap-3">
                          <div className="space-y-1">
                            <p className="text-xs text-muted-foreground">Existing</p>
                            <p className="text-foreground">{row.existingTransaction.date}</p>
                            <p className="text-foreground">{row.existingTransaction.description}</p>
                            <p className="text-foreground">
                              {formatAmount(row.existingTransaction.amount)}
                            </p>
                          </div>
                          <div className="space-y-1">
                            <p className="text-xs text-muted-foreground">Incoming</p>
                            <p className="text-foreground">{row.date}</p>
                            <p className="text-foreground">{row.description}</p>
                            <p className="text-foreground">{formatAmount(row.amount)}</p>
                          </div>
                        </div>
                        <div className="flex items-center gap-4 pt-1">
                          <label className="flex items-center gap-1.5 text-foreground">
                            <input
                              type="radio"
                              name={`decision-${index}`}
                              checked={decisions.get(index) === "Keep"}
                              onChange={() => setRowDecision(index, "Keep")}
                              className="accent-primary focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background focus-visible:outline-none"
                            />
                            Keep (import anyway)
                          </label>
                          <label className="flex items-center gap-1.5 text-foreground">
                            <input
                              type="radio"
                              name={`decision-${index}`}
                              checked={decisions.get(index) === "Skip"}
                              onChange={() => setRowDecision(index, "Skip")}
                              className="accent-primary focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background focus-visible:outline-none"
                            />
                            Skip (don't import)
                          </label>
                        </div>
                      </div>
                    ) : (
                      <div className="flex items-center justify-between text-foreground">
                        <span>{row.date}</span>
                        <span className="flex-1 truncate px-3">{row.description}</span>
                        <span>{formatAmount(row.amount)}</span>
                      </div>
                    )}
                    </Card>
                  </li>
                ))}
              </ul>

              {commitError && <p className="text-sm text-destructive">{commitError}</p>}

              <Button
                type="button"
                onClick={handleContinue}
                disabled={!allDuplicatesDecided || committing}
                className="w-full"
              >
                {committing ? "Importing…" : "Continue"}
              </Button>
            </div>
          ) : accountsLoading ? (
            <p className="text-center text-sm text-muted-foreground animate-[fade-slide-in_600ms_ease-out_both]">
              Loading…
            </p>
          ) : accounts.length === 0 ? (
            <Card
              className="gap-3 p-3 text-center text-sm text-muted-foreground animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "50ms" }}
            >
              <p>You need to add an account before you can import a statement.</p>
              <Button asChild className="mx-auto">
                <a href="/settings">Go to Settings</a>
              </Button>
            </Card>
          ) : (
            <form
              onSubmit={handleSubmit}
              className="space-y-4 animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "50ms" }}
            >
              <div className="space-y-1">
                <label htmlFor="accountId" className="text-sm text-foreground">
                  Account
                </label>
                <select
                  id="accountId"
                  required
                  value={accountId}
                  onChange={(e) => setAccountId(e.target.value)}
                  className={selectClassName}
                >
                  <option value="" disabled>
                    Select an account…
                  </option>
                  {accounts.map((account) => (
                    <option key={account.id} value={account.id}>
                      {account.bankName} — {account.accountNumber}
                    </option>
                  ))}
                </select>
              </div>

              <div className="space-y-1">
                <label htmlFor="file" className="text-sm text-foreground">
                  Bank statement (CSV or PDF)
                </label>
                <Input
                  id="file"
                  type="file"
                  accept=".csv,.pdf"
                  required
                  onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                  className="h-auto cursor-pointer p-1.5 file:mr-2 file:cursor-pointer file:rounded-sm file:border-0 file:bg-primary file:px-3 file:py-1 file:text-sm file:font-medium file:text-primary-foreground"
                />
              </div>

              {showBankPicker && (
                <div className="space-y-1">
                  <label htmlFor="bank" className="text-sm text-foreground">
                    Bank
                  </label>
                  <select
                    id="bank"
                    required
                    value={bank}
                    onChange={(e) => setBank(e.target.value)}
                    className={selectClassName}
                  >
                    <option value="" disabled>
                      Select a bank…
                    </option>
                    {SUPPORTED_BANKS.map((b) => (
                      <option key={b} value={b}>
                        {b}
                      </option>
                    ))}
                  </select>
                </div>
              )}

              {error && <p className="text-sm text-destructive">{error}</p>}

              <Button
                type="submit"
                disabled={submitting || !file || !accountId}
                className="w-full"
              >
                {submitting ? "Uploading…" : "Upload"}
              </Button>
            </form>
          )}
        </div>
      </main>
    </>
  );
}
