import { useState } from "react";
import { redirect } from "react-router";
import { apiFetch, ApiError } from "../lib/api";
import { AppHeader } from "../components/AppHeader";

export async function clientLoader() {
  const res = await fetch("/api/auth/me", { credentials: "include" });
  if (!res.ok) throw redirect("/login");
  return null;
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

interface ImportParseResponse {
  bank: string;
  rows: ImportParseRow[];
  skippedErrorCount: number;
}

interface ImportSummaryDto {
  importBatchId: string;
  bank: string;
  importedAtUtc: string;
  importedCount: number;
  skippedDuplicateCount: number;
  skippedErrorCount: number;
}

// Only mBank is supported today — extend this list as more parsers ship.
const SUPPORTED_BANKS = ["mBank"];

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
  const [file, setFile] = useState<File | null>(null);
  const [bank, setBank] = useState("");
  const [showBankPicker, setShowBankPicker] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<ImportParseResponse | null>(null);
  // Keyed by row index into result.rows — only duplicate rows ever get an entry;
  // non-duplicate rows are implicitly "keep" and never need a decision.
  const [decisions, setDecisions] = useState<Map<number, RowDecisionValue>>(new Map());
  const [committing, setCommitting] = useState(false);
  const [commitError, setCommitError] = useState<string | null>(null);
  const [summary, setSummary] = useState<ImportSummaryDto | null>(null);

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!file) return;

    setError(null);
    setSubmitting(true);
    try {
      const formData = new FormData();
      formData.append("file", file);
      if (showBankPicker && bank) {
        formData.append("bank", bank);
      }

      // apiFetch does not force a Content-Type header, so the browser sets the
      // multipart boundary itself — do not set Content-Type here.
      const response = await apiFetch<ImportParseResponse>("/import/parse", {
        method: "POST",
        body: formData,
      });
      setResult(response);
      setDecisions(new Map());
    } catch (err) {
      // /import/parse only ever returns 400 when no parser recognized the file and no
      // `bank` fallback was given — any other failure (network, 401, 500) shouldn't
      // reveal the picker, since retrying with a bank won't fix those.
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

  async function handleContinue() {
    if (!result) return;

    setCommitError(null);
    setCommitting(true);
    try {
      const rows = result.rows.map((row, index) => ({
        Date: row.date,
        Description: row.description,
        Amount: row.amount,
        // Non-duplicate rows never get a decisions entry — they're implicitly kept.
        Decision: decisions.get(index) ?? "Keep",
      }));

      const response = await apiFetch<ImportSummaryDto>("/import/commit", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          Bank: result.bank,
          SkippedErrorCount: result.skippedErrorCount,
          Rows: rows,
        }),
      });
      setSummary(response);
    } catch (err) {
      setCommitError(await extractErrorMessage(err));
    } finally {
      setCommitting(false);
    }
  }

  return (
    <>
      <AppHeader authenticated />
      <main className="flex items-center justify-center pb-4">
        <div className={`w-full space-y-6 px-4 ${result ? "max-w-2xl" : "max-w-[300px]"}`}>
          <h1
            className="text-center text-lg font-semibold text-gray-200 animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "0ms" }}
          >
            Import statement
          </h1>

          {summary ? (
            <div
              className="space-y-4 animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "50ms" }}
            >
              <div className="space-y-1 rounded-lg border border-gray-800 p-3 text-sm text-gray-200">
                <p>
                  Imported <strong>{summary.importedCount}</strong> row(s) from{" "}
                  <strong>{summary.bank}</strong>.
                </p>
                {summary.skippedDuplicateCount > 0 && (
                  <p className="text-gray-400">
                    {summary.skippedDuplicateCount} duplicate row(s) skipped.
                  </p>
                )}
                {summary.skippedErrorCount > 0 && (
                  <p className="text-gray-400">
                    {summary.skippedErrorCount} row(s) skipped due to parse errors.
                  </p>
                )}
              </div>
            </div>
          ) : result ? (
            <div
              className="space-y-4 animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "50ms" }}
            >
              <div className="space-y-1 rounded-lg border border-gray-800 p-3 text-sm text-gray-200">
                <p>
                  Parsed <strong>{result.rows.length}</strong> row(s) from{" "}
                  <strong>{result.bank}</strong>.
                </p>
                {result.skippedErrorCount > 0 && (
                  <p className="text-gray-400">
                    {result.skippedErrorCount} row(s) skipped due to parse errors.
                  </p>
                )}
                {duplicateIndexes.length > 0 && (
                  <p className="text-gray-400">
                    {duplicateIndexes.length} possible duplicate(s) need a decision
                    before you can continue.
                  </p>
                )}
              </div>

              <ul className="space-y-2">
                {result.rows.map((row, index) => (
                  <li
                    key={index}
                    className={`rounded-lg border p-3 text-sm ${
                      row.isDuplicate ? "border-amber-700/60 bg-amber-950/20" : "border-gray-800"
                    }`}
                  >
                    {row.isDuplicate && row.existingTransaction ? (
                      <div className="space-y-2">
                        <p className="text-xs font-medium uppercase tracking-wide text-amber-500">
                          Possible duplicate
                        </p>
                        <div className="grid grid-cols-2 gap-3">
                          <div className="space-y-1">
                            <p className="text-xs text-gray-500">Existing</p>
                            <p className="text-gray-200">{row.existingTransaction.date}</p>
                            <p className="text-gray-200">{row.existingTransaction.description}</p>
                            <p className="text-gray-200">
                              {formatAmount(row.existingTransaction.amount)}
                            </p>
                          </div>
                          <div className="space-y-1">
                            <p className="text-xs text-gray-500">Incoming</p>
                            <p className="text-gray-200">{row.date}</p>
                            <p className="text-gray-200">{row.description}</p>
                            <p className="text-gray-200">{formatAmount(row.amount)}</p>
                          </div>
                        </div>
                        <div className="flex items-center gap-4 pt-1">
                          <label className="flex items-center gap-1.5 text-gray-200">
                            <input
                              type="radio"
                              name={`decision-${index}`}
                              checked={decisions.get(index) === "Keep"}
                              onChange={() => setRowDecision(index, "Keep")}
                              className="accent-brand-500"
                            />
                            Keep (import anyway)
                          </label>
                          <label className="flex items-center gap-1.5 text-gray-200">
                            <input
                              type="radio"
                              name={`decision-${index}`}
                              checked={decisions.get(index) === "Skip"}
                              onChange={() => setRowDecision(index, "Skip")}
                              className="accent-brand-500"
                            />
                            Skip (don't import)
                          </label>
                        </div>
                      </div>
                    ) : (
                      <div className="flex items-center justify-between text-gray-200">
                        <span>{row.date}</span>
                        <span className="flex-1 truncate px-3">{row.description}</span>
                        <span>{formatAmount(row.amount)}</span>
                      </div>
                    )}
                  </li>
                ))}
              </ul>

              {commitError && <p className="text-sm text-red-600">{commitError}</p>}

              <button
                type="button"
                onClick={handleContinue}
                disabled={!allDuplicatesDecided || committing}
                className="w-full rounded-lg bg-brand-500 p-2 text-sm font-medium text-white transition-colors duration-200 hover:bg-brand-600 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
              >
                {committing ? "Importing…" : "Continue"}
              </button>
            </div>
          ) : (
            <form
              onSubmit={handleSubmit}
              className="space-y-4 animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "50ms" }}
            >
              <div className="space-y-1">
                <label htmlFor="file" className="text-sm text-gray-200">
                  Bank statement CSV
                </label>
                <input
                  id="file"
                  type="file"
                  accept=".csv"
                  required
                  onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                  className="w-full rounded-lg border border-gray-700 bg-transparent p-2 text-sm text-gray-200 file:mr-2 file:rounded-md file:border-0 file:bg-brand-500 file:px-3 file:py-1.5 file:text-white focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
                />
              </div>

              {showBankPicker && (
                <div className="space-y-1">
                  <label htmlFor="bank" className="text-sm text-gray-200">
                    Bank
                  </label>
                  <select
                    id="bank"
                    required
                    value={bank}
                    onChange={(e) => setBank(e.target.value)}
                    className="w-full rounded-lg border border-gray-700 bg-transparent p-2 text-sm text-gray-200 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
                  >
                    <option value="" disabled>
                      Select a bank…
                    </option>
                    {SUPPORTED_BANKS.map((b) => (
                      <option key={b} value={b} className="bg-gray-900">
                        {b}
                      </option>
                    ))}
                  </select>
                </div>
              )}

              {error && <p className="text-sm text-red-600">{error}</p>}

              <button
                type="submit"
                disabled={submitting || !file}
                className="w-full rounded-lg bg-brand-500 p-2 text-sm font-medium text-white transition-colors duration-200 hover:bg-brand-600 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
              >
                {submitting ? "Uploading…" : "Upload"}
              </button>
            </form>
          )}
        </div>
      </main>
    </>
  );
}
