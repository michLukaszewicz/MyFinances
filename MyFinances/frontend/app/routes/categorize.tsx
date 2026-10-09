import { useEffect, useState } from "react";
import { redirect } from "react-router";
import { apiFetch, ApiError } from "../lib/api";
import { notifyUncategorizedChanged } from "../lib/uncategorized";
import { categoriesForAmount, type CategoryDto } from "../lib/categories";
import { AppHeader } from "../components/AppHeader";
import { Button } from "../components/ui/button";
import { Card, CardContent } from "../components/ui/card";
import { Input, selectClassName } from "../components/ui/input";

export async function clientLoader() {
  const res = await fetch("/api/auth/me", { credentials: "include" });
  if (!res.ok) throw redirect("/login");
  return null;
}

// Mirrors the backend's CategorizationContracts.cs TransactionQueueItemDto.
interface TransactionQueueItemDto {
  id: string;
  date: string;
  description: string;
  amount: number;
  bankName: string;
  accountNumber: string;
  categoryId: string | null;
  categoryName: string | null;
  isInternalTransfer: boolean;
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

// The backend answers a delete of a category that is still used by transactions with a 409 carrying
// the number of transactions that would go back to the queue; null for any other failure.
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

export default function Categorize() {
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [queue, setQueue] = useState<TransactionQueueItemDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [upNextCategoryId, setUpNextCategoryId] = useState("");
  const [upNextTransfer, setUpNextTransfer] = useState(false);
  const [upNextError, setUpNextError] = useState<string | null>(null);
  const [savingUpNext, setSavingUpNext] = useState(false);

  const [showCategoryManager, setShowCategoryManager] = useState(false);
  const [newCategoryName, setNewCategoryName] = useState("");
  const [newCategoryKind, setNewCategoryKind] = useState<"expense" | "income">("expense");
  const [categoryFormError, setCategoryFormError] = useState<string | null>(null);
  const [addingCategory, setAddingCategory] = useState(false);
  const [confirmingCategoryDeleteId, setConfirmingCategoryDeleteId] = useState<string | null>(null);
  const [categoryDeleteError, setCategoryDeleteError] = useState<string | null>(null);
  const [deletingCategory, setDeletingCategory] = useState(false);
  const [categoryUsageWarning, setCategoryUsageWarning] = useState<{
    categoryId: string;
    transactionCount: number;
  } | null>(null);

  async function loadAll() {
    const [categoryList, queueList] = await Promise.all([
      apiFetch<CategoryDto[]>("/categorization/categories"),
      apiFetch<TransactionQueueItemDto[]>("/categorization/queue"),
    ]);
    setCategories(categoryList);
    setQueue(queueList);
  }

  useEffect(() => {
    async function loadInitial() {
      try {
        await loadAll();
      } catch (err) {
        setLoadError(await extractErrorMessage(err));
      } finally {
        setLoading(false);
      }
    }
    void loadInitial();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const upNext = queue[0] ?? null;

  useEffect(() => {
    setUpNextCategoryId("");
    setUpNextTransfer(false);
    setUpNextError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [upNext?.id]);

  async function handleSaveUpNext(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!upNext) return;
    if (!upNextTransfer && !upNextCategoryId) return;

    setUpNextError(null);
    setSavingUpNext(true);
    try {
      await apiFetch<TransactionQueueItemDto>(`/categorization/transactions/${upNext.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          CategoryId: upNextTransfer ? null : upNextCategoryId,
          IsInternalTransfer: upNextTransfer,
        }),
      });
      await loadAll();
      notifyUncategorizedChanged();
    } catch (err) {
      setUpNextError(await extractErrorMessage(err));
    } finally {
      setSavingUpNext(false);
    }
  }

  async function handleAddCategory(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const name = newCategoryName.trim();
    if (!name) return;

    setCategoryFormError(null);
    setAddingCategory(true);
    try {
      await apiFetch<CategoryDto>("/categorization/categories", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ Name: name, Kind: newCategoryKind }),
      });
      await loadAll();
      setNewCategoryName("");
    } catch (err) {
      setCategoryFormError(await extractErrorMessage(err));
    } finally {
      setAddingCategory(false);
    }
  }

  async function handleDeleteCategory(id: string, uncategorizeTransactions = false) {
    setCategoryDeleteError(null);
    setDeletingCategory(true);
    try {
      const query = uncategorizeTransactions ? "?uncategorizeTransactions=true" : "";
      await apiFetch(`/categorization/categories/${id}${query}`, { method: "DELETE" });
      await loadAll();
      notifyUncategorizedChanged();
      setConfirmingCategoryDeleteId(null);
      setCategoryUsageWarning(null);
    } catch (err) {
      const transactionCount = await extractTransactionCount(err);
      setConfirmingCategoryDeleteId(null);
      if (transactionCount !== null) {
        setCategoryUsageWarning({ categoryId: id, transactionCount });
      } else {
        setCategoryUsageWarning(null);
        setCategoryDeleteError(await extractErrorMessage(err));
      }
    } finally {
      setDeletingCategory(false);
    }
  }

  function renderCategoryList(kind: "expense" | "income", title: string) {
    const items = categories.filter((c) => c.kind === kind);
    return (
      <div className="space-y-2">
        <h3 className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{title}</h3>
        {items.length === 0 ? (
          <p className="text-sm text-muted-foreground">No {kind} categories.</p>
        ) : (
          <ul className="space-y-2">
            {items.map((category) => (
              <li key={category.id}>
                <Card className="flex-row items-center justify-between gap-2 p-3 text-sm text-foreground">
                  <span>{category.name}</span>
                  {confirmingCategoryDeleteId === category.id ? (
                    <div className="flex items-center gap-2">
                      <Button
                        type="button"
                        variant="destructive-ghost"
                        size="sm"
                        disabled={deletingCategory}
                        onClick={() => void handleDeleteCategory(category.id)}
                      >
                        Confirm delete
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="text-muted-foreground"
                        onClick={() => setConfirmingCategoryDeleteId(null)}
                      >
                        Cancel
                      </Button>
                    </div>
                  ) : (
                    <Button
                      type="button"
                      variant="destructive-ghost"
                      size="sm"
                      onClick={() => {
                        setCategoryDeleteError(null);
                        setCategoryUsageWarning(null);
                        setConfirmingCategoryDeleteId(category.id);
                      }}
                    >
                      Delete
                    </Button>
                  )}
                </Card>
                {categoryUsageWarning?.categoryId === category.id && (
                  <div
                    role="alert"
                    className="mt-2 space-y-3 rounded-lg border border-warning p-3 text-sm text-foreground"
                  >
                    <p>
                      This category is used by {categoryUsageWarning.transactionCount}{" "}
                      {categoryUsageWarning.transactionCount === 1 ? "transaction" : "transactions"}. If you delete
                      it, {categoryUsageWarning.transactionCount === 1 ? "that transaction" : "those transactions"}{" "}
                      will become uncategorized and appear in the list above again.
                    </p>
                    <div className="flex gap-2">
                      <Button
                        type="button"
                        variant="destructive-ghost"
                        size="sm"
                        disabled={deletingCategory}
                        onClick={() => void handleDeleteCategory(category.id, true)}
                      >
                        {deletingCategory ? "Deleting…" : "Delete category and uncategorize"}
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="text-muted-foreground"
                        disabled={deletingCategory}
                        onClick={() => setCategoryUsageWarning(null)}
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
    );
  }

  return (
    <>
      <AppHeader authenticated />
      <main className="flex items-center justify-center pt-16 pb-4">
        <div className="w-full max-w-2xl space-y-8 px-4">
          <h1 className="text-center text-lg font-semibold text-foreground">Categorize</h1>

          {loading ? (
            <p className="text-center text-sm text-muted-foreground">Loading…</p>
          ) : loadError ? (
            <p className="text-center text-sm text-destructive">{loadError}</p>
          ) : (
            <>
              <div className="flex justify-end">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  aria-expanded={showCategoryManager}
                  onClick={() => setShowCategoryManager((open) => !open)}
                >
                  {showCategoryManager ? "Hide categories" : "Manage categories"}
                </Button>
              </div>

              {showCategoryManager && (
                <section className="space-y-4">
                  <h2 className="text-sm font-medium text-foreground">Manage categories</h2>
                  <Card>
                    <CardContent>
                      <form onSubmit={handleAddCategory} className="space-y-3">
                        <div className="flex flex-wrap items-end gap-2">
                          <div className="min-w-40 flex-1 space-y-1">
                            <label htmlFor="newCategoryName" className="text-sm text-foreground">
                              New category name
                            </label>
                            <Input
                              id="newCategoryName"
                              type="text"
                              maxLength={50}
                              value={newCategoryName}
                              onChange={(e) => setNewCategoryName(e.target.value)}
                            />
                          </div>
                          <div className="space-y-1">
                            <label htmlFor="newCategoryKind" className="text-sm text-foreground">
                              Type
                            </label>
                            <select
                              id="newCategoryKind"
                              value={newCategoryKind}
                              onChange={(e) => setNewCategoryKind(e.target.value as "expense" | "income")}
                              className={selectClassName}
                            >
                              <option value="expense">Expense</option>
                              <option value="income">Income</option>
                            </select>
                          </div>
                          <Button type="submit" disabled={addingCategory || !newCategoryName.trim()}>
                            {addingCategory ? "Adding…" : "Add category"}
                          </Button>
                        </div>
                        {categoryFormError && (
                          <p role="alert" className="text-sm text-destructive">
                            {categoryFormError}
                          </p>
                        )}
                      </form>
                    </CardContent>
                  </Card>
                  {categoryDeleteError && (
                    <p role="alert" className="text-sm text-destructive">
                      {categoryDeleteError}
                    </p>
                  )}
                  {renderCategoryList("expense", "Expense categories")}
                  {renderCategoryList("income", "Income categories")}
                </section>
              )}

              <section className="space-y-2">
                {upNext && <h2 className="text-sm font-medium text-foreground">Up next</h2>}
                {upNext ? (
                  <Card>
                    <CardContent>
                  <form onSubmit={handleSaveUpNext} className="space-y-4">
                    <div className="flex items-center justify-between text-sm text-foreground">
                      <span>
                        {upNext.date} — {upNext.description}
                      </span>
                      <span className={upNext.amount < 0 ? "text-destructive" : "text-success"}>
                        {upNext.amount.toFixed(2)}
                      </span>
                    </div>
                    <p className="text-xs text-muted-foreground">
                      {upNext.bankName} — {upNext.accountNumber}
                    </p>

                    <div className="space-y-1">
                      <label htmlFor="upNextCategory" className="text-sm text-foreground">
                        Category
                      </label>
                      <select
                        id="upNextCategory"
                        value={upNextCategoryId}
                        disabled={upNextTransfer}
                        onChange={(e) => setUpNextCategoryId(e.target.value)}
                        className={selectClassName}
                      >
                        <option value="">Select a category…</option>
                        {categoriesForAmount(categories, upNext.amount).map((category) => (
                          <option key={category.id} value={category.id}>
                            {category.name}
                          </option>
                        ))}
                      </select>
                    </div>

                    <label className="flex items-center gap-2 text-sm text-foreground">
                      <input
                        type="checkbox"
                        checked={upNextTransfer}
                        onChange={(e) => setUpNextTransfer(e.target.checked)}
                        className="h-4 w-4 cursor-pointer rounded border-input accent-primary outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
                      />
                      This is an internal transfer between my own accounts
                    </label>

                    {upNextError && <p className="text-sm text-destructive">{upNextError}</p>}

                    <Button
                      type="submit"
                      disabled={savingUpNext || (!upNextTransfer && !upNextCategoryId)}
                    >
                      {savingUpNext ? "Saving…" : "Save"}
                    </Button>
                  </form>
                    </CardContent>
                  </Card>
                ) : (
                  <p className="text-center text-sm text-muted-foreground">Yay, all done! 🎉</p>
                )}
              </section>

              {queue.length > 1 && (
                <section className="space-y-2">
                  <h2 className="text-sm font-medium text-foreground">Remaining</h2>
                  <ul className="space-y-2">
                    {queue.slice(1).map((item) => (
                      <li key={item.id}>
                        <Card className="gap-1 p-3 text-sm text-foreground">
                        <div className="flex items-center justify-between">
                          <span>
                            {item.date} — {item.description}
                          </span>
                          <span className={item.amount < 0 ? "text-destructive" : "text-success"}>
                            {item.amount.toFixed(2)}
                          </span>
                        </div>
                        <p className="text-xs text-muted-foreground">
                          {item.bankName} — {item.accountNumber}
                        </p>
                        </Card>
                      </li>
                    ))}
                  </ul>
                </section>
              )}
            </>
          )}
        </div>
      </main>
    </>
  );
}
