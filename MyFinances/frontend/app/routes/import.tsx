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

// Only mBank is supported today — extend this list as more parsers ship.
const SUPPORTED_BANKS = ["mBank"];

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

  return (
    <>
      <AppHeader authenticated />
      <main className="flex items-center justify-center pb-4">
        <div className="max-w-[300px] w-full space-y-6 px-4">
          <h1
            className="text-center text-lg font-semibold text-gray-200 animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "0ms" }}
          >
            Import statement
          </h1>

          {result ? (
            <div
              className="space-y-2 rounded-lg border border-gray-800 p-3 text-sm text-gray-200 animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "50ms" }}
            >
              <p>
                Parsed <strong>{result.rows.length}</strong> row(s) from{" "}
                <strong>{result.bank}</strong>.
              </p>
              {result.skippedErrorCount > 0 && (
                <p className="text-gray-400">
                  {result.skippedErrorCount} row(s) skipped due to parse errors.
                </p>
              )}
              <p className="text-gray-500">
                The duplicate review screen will replace this placeholder in a
                later step.
              </p>
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
