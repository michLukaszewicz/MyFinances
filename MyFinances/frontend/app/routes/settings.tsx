import { useEffect, useRef, useState } from "react";
import { redirect } from "react-router";
import { apiFetch, ApiError } from "../lib/api";
import { notifyUncategorizedChanged } from "../lib/uncategorized";
import { AppHeader } from "../components/AppHeader";
import { Button } from "../components/ui/button";
import { Card, CardContent } from "../components/ui/card";
import { Input, selectClassName } from "../components/ui/input";

export async function clientLoader() {
  const res = await fetch("/api/auth/me", { credentials: "include" });
  if (!res.ok) throw redirect("/login");
  return null;
}

// Mirrors the backend's AccountContracts.cs AccountDto.
interface AccountDto {
  id: string;
  bankName: string;
  accountNumber: string;
  bank: string;
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

// The backend answers a delete of an account that still has transactions with a 409 carrying the
// number of transactions that would be lost; null for any other failure.
async function extractTransactionCount(error: unknown): Promise<number | null> {
  if (error instanceof ApiError && error.response.status === 409) {
    try {
      const body = await error.response.clone().json();
      if (typeof body?.transactionCount === "number") {
        return body.transactionCount;
      }
    } catch {
      // fall through to null
    }
  }
  return null;
}

export default function Settings() {
  const [accounts, setAccounts] = useState<AccountDto[]>([]);
  const [loading, setLoading] = useState(true);

  const [isFormOpen, setIsFormOpen] = useState(false);
  const [bankName, setBankName] = useState("");
  const [bank, setBank] = useState("");
  const [bankOptions, setBankOptions] = useState<string[]>([]);
  const [accountNumber, setAccountNumber] = useState("");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [transactionDeleteWarning, setTransactionDeleteWarning] = useState<{
    accountId: string;
    transactionCount: number;
  } | null>(null);

  const bankNameInputRef = useRef<HTMLInputElement>(null);

  async function loadAccounts() {
    const list = await apiFetch<AccountDto[]>("/accounts/");
    setAccounts(list);
  }

  useEffect(() => {
    async function loadInitial() {
      const options = await apiFetch<{ bankNames: string[] }>("/accounts/banks");
      setBankOptions(options.bankNames);
      await loadAccounts();
      setLoading(false);
    }
    void loadInitial();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function closeForm() {
    setIsFormOpen(false);
    setBankName("");
    setBank("");
    setAccountNumber("");
    setEditingId(null);
    setFormError(null);
  }

  function startAdd() {
    setEditingId(null);
    setBankName("");
    setBank("");
    setAccountNumber("");
    setFormError(null);
    setIsFormOpen(true);
    // Wait for the form to mount before focusing it.
    requestAnimationFrame(() => bankNameInputRef.current?.focus());
  }

  function startEdit(account: AccountDto) {
    setEditingId(account.id);
    setBankName(account.bankName);
    setBank(account.bank);
    setAccountNumber(account.accountNumber);
    setFormError(null);
    setIsFormOpen(true);
    // The form sits above a potentially long account list — scroll it into view and
    // focus the first field once mounted, so clicking "Edit" on a far-down row doesn't
    // leave the user looking at an unchanged screen.
    requestAnimationFrame(() => {
      bankNameInputRef.current?.scrollIntoView({ behavior: "smooth", block: "center" });
      bankNameInputRef.current?.focus();
    });
  }

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!bankName || !bank || !accountNumber) return;

    setFormError(null);
    setSubmitting(true);
    try {
      if (editingId) {
        await apiFetch<AccountDto>(`/accounts/${editingId}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ BankName: bankName, Bank: bank, AccountNumber: accountNumber }),
        });
      } else {
        await apiFetch<AccountDto>("/accounts/", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ BankName: bankName, Bank: bank, AccountNumber: accountNumber }),
        });
      }
      await loadAccounts();
      closeForm();
    } catch (err) {
      setFormError(await extractErrorMessage(err));
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete(id: string, deleteTransactions = false) {
    setDeleteError(null);
    setDeleting(true);
    try {
      const query = deleteTransactions ? "?deleteTransactions=true" : "";
      await apiFetch(`/accounts/${id}${query}`, { method: "DELETE" });
      notifyUncategorizedChanged();
      await loadAccounts();
      setConfirmingDeleteId(null);
      setTransactionDeleteWarning(null);
    } catch (err) {
      const transactionCount = await extractTransactionCount(err);
      if (transactionCount !== null) {
        setConfirmingDeleteId(null);
        setTransactionDeleteWarning({ accountId: id, transactionCount });
      } else {
        setConfirmingDeleteId(null);
        setTransactionDeleteWarning(null);
        setDeleteError(await extractErrorMessage(err));
      }
    } finally {
      setDeleting(false);
    }
  }

  return (
    <>
      <AppHeader authenticated />
      <main className="flex items-center justify-center pt-16 pb-4">
        <div className="w-full max-w-2xl space-y-6 px-4">
          <h1 className="text-center text-lg font-semibold text-foreground">Settings</h1>

          {isFormOpen ? (
            <Card>
              <CardContent>
            <form onSubmit={handleSubmit} className="space-y-4">
              <h2 className="text-sm font-medium text-foreground">
                {editingId ? "Edit account" : "Add account"}
              </h2>

              <div className="space-y-1">
                <label htmlFor="bankName" className="text-sm text-foreground">
                  Account name
                </label>
                <Input
                  id="bankName"
                  ref={bankNameInputRef}
                  type="text"
                  required
                  value={bankName}
                  onChange={(e) => setBankName(e.target.value)}
                />
              </div>

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
                  {bankOptions.map((b) => (
                    <option key={b} value={b}>
                      {b}
                    </option>
                  ))}
                </select>
                <p className="text-xs text-muted-foreground">
                  Used to warn you when an imported file comes from a different bank. Choose Other
                  to skip that check.
                </p>
              </div>

              <div className="space-y-1">
                <label htmlFor="accountNumber" className="text-sm text-foreground">
                  Account number
                </label>
                <Input
                  id="accountNumber"
                  type="text"
                  required
                  value={accountNumber}
                  onChange={(e) => setAccountNumber(e.target.value)}
                />
              </div>

              {formError && <p className="text-sm text-destructive">{formError}</p>}

              <div className="flex gap-2">
                <Button type="submit" disabled={submitting}>
                  {submitting ? "Saving…" : editingId ? "Save changes" : "Add account"}
                </Button>
                <Button type="button" variant="outline" onClick={closeForm}>
                  Cancel
                </Button>
              </div>
            </form>
              </CardContent>
            </Card>
          ) : (
            <Button type="button" onClick={startAdd} className="w-full">
              Add account
            </Button>
          )}

          <div className="space-y-2">
            <h2 className="text-sm font-medium text-foreground">Your accounts</h2>
            {deleteError && (
              <p role="alert" className="text-sm text-destructive">
                {deleteError}
              </p>
            )}
            {loading ? (
              <p className="text-sm text-muted-foreground">Loading…</p>
            ) : accounts.length === 0 ? (
              <p className="text-sm text-muted-foreground">You haven't added any accounts yet.</p>
            ) : (
              <ul className="space-y-2">
                {accounts.map((account) => (
                  <li key={account.id}>
                    <Card className="flex-row items-center justify-between gap-2 p-3 text-sm text-foreground">
                    <span>
                      {account.bankName} — {account.accountNumber}
                      <span className="ml-2 text-xs text-muted-foreground">({account.bank})</span>
                    </span>
                    <div className="flex items-center gap-2">
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        onClick={() => startEdit(account)}
                      >
                        Edit
                      </Button>
                      {confirmingDeleteId === account.id ? (
                        <>
                          <Button
                            type="button"
                            variant="destructive-ghost"
                            size="sm"
                            disabled={deleting}
                            onClick={() => void handleDelete(account.id)}
                          >
                            Confirm delete
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            className="text-muted-foreground"
                            onClick={() => setConfirmingDeleteId(null)}
                          >
                            Cancel
                          </Button>
                        </>
                      ) : (
                        <Button
                          type="button"
                          variant="destructive-ghost"
                          size="sm"
                          onClick={() => {
                          setDeleteError(null);
                          setTransactionDeleteWarning(null);
                          setConfirmingDeleteId(account.id);
                        }}
                        >
                          Delete
                        </Button>
                      )}
                    </div>
                    </Card>
                    {transactionDeleteWarning?.accountId === account.id && (
                      <div
                        role="alert"
                        className="mt-2 space-y-3 rounded-lg border border-warning p-3 text-sm text-foreground"
                      >
                        <p>
                          This account has {transactionDeleteWarning.transactionCount}{" "}
                          {transactionDeleteWarning.transactionCount === 1
                            ? "transaction"
                            : "transactions"}
                          . Deleting it will permanently delete the account together with all its
                          transactions and import history. This cannot be undone.
                        </p>
                        <div className="flex gap-2">
                          <Button
                            type="button"
                            variant="destructive-ghost"
                            size="sm"
                            disabled={deleting}
                            onClick={() => void handleDelete(account.id, true)}
                          >
                            {deleting ? "Deleting…" : "Delete account and transactions"}
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            className="text-muted-foreground"
                            disabled={deleting}
                            onClick={() => setTransactionDeleteWarning(null)}
                          >
                            Cancel
                          </Button>
                        </div>
                      </div>
                    )}
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
