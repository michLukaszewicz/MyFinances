import { useEffect, useState } from "react";
import { redirect } from "react-router";
import { apiFetch, ApiError } from "../lib/api";
import { AppHeader } from "../components/AppHeader";

export async function clientLoader() {
  const res = await fetch("/api/auth/me", { credentials: "include" });
  if (!res.ok) throw redirect("/login");
  return null;
}

// Mirrors the backend's AccountContracts.cs (AccountDto / BankOptionsResponse).
interface AccountDto {
  id: string;
  bankName: string;
  accountNumber: string;
}

interface BankOptionsResponse {
  bankNames: string[];
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

export default function Settings() {
  const [banks, setBanks] = useState<string[]>([]);
  const [accounts, setAccounts] = useState<AccountDto[]>([]);
  const [loading, setLoading] = useState(true);

  const [bankName, setBankName] = useState("");
  const [accountNumber, setAccountNumber] = useState("");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null);

  async function loadAccounts() {
    const list = await apiFetch<AccountDto[]>("/accounts/");
    setAccounts(list);
  }

  useEffect(() => {
    async function loadInitial() {
      const [bankOptions] = await Promise.all([
        apiFetch<BankOptionsResponse>("/accounts/banks"),
        loadAccounts(),
      ]);
      setBanks(bankOptions.bankNames);
      setLoading(false);
    }
    void loadInitial();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function resetForm() {
    setBankName("");
    setAccountNumber("");
    setEditingId(null);
    setFormError(null);
  }

  function startEdit(account: AccountDto) {
    setEditingId(account.id);
    setBankName(account.bankName);
    setAccountNumber(account.accountNumber);
    setFormError(null);
  }

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!bankName || !accountNumber) return;

    setFormError(null);
    setSubmitting(true);
    try {
      if (editingId) {
        await apiFetch<AccountDto>(`/accounts/${editingId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ BankName: bankName, AccountNumber: accountNumber }),
        });
      } else {
        await apiFetch<AccountDto>("/accounts/", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ BankName: bankName, AccountNumber: accountNumber }),
        });
      }
      await loadAccounts();
      resetForm();
    } catch (err) {
      setFormError(await extractErrorMessage(err));
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete(id: string) {
    await apiFetch(`/accounts/${id}`, { method: "DELETE" });
    setConfirmingDeleteId(null);
    await loadAccounts();
  }

  return (
    <>
      <AppHeader authenticated />
      <main className="flex items-center justify-center pb-4">
        <div className="w-full max-w-2xl space-y-6 px-4">
          <h1 className="text-center text-lg font-semibold text-gray-200">Settings</h1>

          <form onSubmit={handleSubmit} className="space-y-4 rounded-lg border border-gray-800 p-4">
            <h2 className="text-sm font-medium text-gray-200">
              {editingId ? "Edit account" : "Add account"}
            </h2>

            <div className="space-y-1">
              <label htmlFor="bankName" className="text-sm text-gray-200">
                Bank
              </label>
              <select
                id="bankName"
                required
                value={bankName}
                onChange={(e) => setBankName(e.target.value)}
                className="w-full rounded-lg border border-gray-700 bg-transparent p-2 text-sm text-gray-200 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
              >
                <option value="" disabled>
                  Select a bank…
                </option>
                {banks.map((b) => (
                  <option key={b} value={b} className="bg-gray-900">
                    {b}
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-1">
              <label htmlFor="accountNumber" className="text-sm text-gray-200">
                Account number
              </label>
              <input
                id="accountNumber"
                type="text"
                required
                value={accountNumber}
                onChange={(e) => setAccountNumber(e.target.value)}
                className="w-full rounded-lg border border-gray-700 bg-transparent p-2 text-sm text-gray-200 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
              />
            </div>

            {formError && <p className="text-sm text-red-600">{formError}</p>}

            <div className="flex gap-2">
              <button
                type="submit"
                disabled={submitting}
                className="rounded-lg bg-brand-500 px-4 py-2 text-sm font-medium text-white transition-colors duration-200 hover:bg-brand-600 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
              >
                {submitting ? "Saving…" : editingId ? "Save changes" : "Add account"}
              </button>
              {editingId && (
                <button
                  type="button"
                  onClick={resetForm}
                  className="rounded-lg border border-gray-700 px-4 py-2 text-sm font-medium text-gray-200 transition-colors hover:bg-white/5"
                >
                  Cancel
                </button>
              )}
            </div>
          </form>

          <div className="space-y-2">
            <h2 className="text-sm font-medium text-gray-200">Your accounts</h2>
            {loading ? (
              <p className="text-sm text-gray-400">Loading…</p>
            ) : accounts.length === 0 ? (
              <p className="text-sm text-gray-400">You haven't added any accounts yet.</p>
            ) : (
              <ul className="space-y-2">
                {accounts.map((account) => (
                  <li
                    key={account.id}
                    className="flex items-center justify-between rounded-lg border border-gray-800 p-3 text-sm text-gray-200"
                  >
                    <span>
                      {account.bankName} — {account.accountNumber}
                    </span>
                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={() => startEdit(account)}
                        className="rounded-md px-2 py-1 text-xs font-medium text-brand-400 transition-colors hover:bg-white/5 hover:text-brand-300"
                      >
                        Edit
                      </button>
                      {confirmingDeleteId === account.id ? (
                        <>
                          <button
                            type="button"
                            onClick={() => void handleDelete(account.id)}
                            className="rounded-md px-2 py-1 text-xs font-medium text-red-500 transition-colors hover:bg-red-950/30"
                          >
                            Confirm delete
                          </button>
                          <button
                            type="button"
                            onClick={() => setConfirmingDeleteId(null)}
                            className="rounded-md px-2 py-1 text-xs font-medium text-gray-400 transition-colors hover:bg-white/5"
                          >
                            Cancel
                          </button>
                        </>
                      ) : (
                        <button
                          type="button"
                          onClick={() => setConfirmingDeleteId(account.id)}
                          className="rounded-md px-2 py-1 text-xs font-medium text-red-500 transition-colors hover:bg-red-950/30"
                        >
                          Delete
                        </button>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      </main>
    </>
  );
}
