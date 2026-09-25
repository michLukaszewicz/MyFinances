import { useEffect, useState } from "react";
import { redirect } from "react-router";
import { apiFetch, ApiError } from "../lib/api";
import { AppHeader } from "../components/AppHeader";

export async function clientLoader() {
  const res = await fetch("/api/auth/me", { credentials: "include" });
  if (!res.ok) throw redirect("/login");
  return null;
}

// Mirrors the backend's CategorizationContracts.cs CategoryDto.
interface CategoryDto {
  id: string;
  name: string;
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

export default function Categorize() {
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [queue, setQueue] = useState<TransactionQueueItemDto[]>([]);
  const [handled, setHandled] = useState<TransactionQueueItemDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [upNextCategoryId, setUpNextCategoryId] = useState("");
  const [upNextTransfer, setUpNextTransfer] = useState(false);
  const [upNextError, setUpNextError] = useState<string | null>(null);
  const [savingUpNext, setSavingUpNext] = useState(false);

  const [savingHandledId, setSavingHandledId] = useState<string | null>(null);
  const [handledErrors, setHandledErrors] = useState<Record<string, string>>({});

  async function loadAll() {
    const [categoryList, queueList, handledList] = await Promise.all([
      apiFetch<CategoryDto[]>("/categorization/categories"),
      apiFetch<TransactionQueueItemDto[]>("/categorization/queue"),
      apiFetch<TransactionQueueItemDto[]>("/categorization/handled"),
    ]);
    setCategories(categoryList);
    setQueue(queueList);
    setHandled(handledList);
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
    } catch (err) {
      setUpNextError(await extractErrorMessage(err));
    } finally {
      setSavingUpNext(false);
    }
  }

  async function handleUpdateHandled(
    item: TransactionQueueItemDto,
    changes: { categoryId?: string | null; isInternalTransfer?: boolean },
  ) {
    setHandledErrors((prev) => ({ ...prev, [item.id]: "" }));
    setSavingHandledId(item.id);
    try {
      await apiFetch<TransactionQueueItemDto>(`/categorization/transactions/${item.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          CategoryId: changes.categoryId ?? item.categoryId,
          IsInternalTransfer: changes.isInternalTransfer ?? item.isInternalTransfer,
        }),
      });
      await loadAll();
    } catch (err) {
      const message = await extractErrorMessage(err);
      setHandledErrors((prev) => ({ ...prev, [item.id]: message }));
    } finally {
      setSavingHandledId(null);
    }
  }

  return (
    <>
      <AppHeader authenticated />
      <main className="flex items-center justify-center pb-4">
        <div className="w-full max-w-2xl space-y-8 px-4">
          <h1 className="text-center text-lg font-semibold text-gray-200">Categorize</h1>

          {loading ? (
            <p className="text-center text-sm text-gray-400">Loading…</p>
          ) : loadError ? (
            <p className="text-center text-sm text-red-600">{loadError}</p>
          ) : (
            <>
              <section className="space-y-2">
                <h2 className="text-sm font-medium text-gray-200">Up next</h2>
                {upNext ? (
                  <form
                    onSubmit={handleSaveUpNext}
                    className="space-y-4 rounded-lg border border-gray-800 p-4"
                  >
                    <div className="flex items-center justify-between text-sm text-gray-200">
                      <span>
                        {upNext.date} — {upNext.description}
                      </span>
                      <span className={upNext.amount < 0 ? "text-red-500" : "text-emerald-500"}>
                        {upNext.amount.toFixed(2)}
                      </span>
                    </div>
                    <p className="text-xs text-gray-400">
                      {upNext.bankName} — {upNext.accountNumber}
                    </p>

                    <div className="space-y-1">
                      <label htmlFor="upNextCategory" className="text-sm text-gray-200">
                        Category
                      </label>
                      <select
                        id="upNextCategory"
                        value={upNextCategoryId}
                        disabled={upNextTransfer}
                        onChange={(e) => setUpNextCategoryId(e.target.value)}
                        className="w-full rounded-lg border border-gray-700 bg-gray-900 p-2 text-sm text-gray-200 [color-scheme:dark] focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500 disabled:opacity-50"
                      >
                        <option value="">Select a category…</option>
                        {categories.map((category) => (
                          <option key={category.id} value={category.id}>
                            {category.name}
                          </option>
                        ))}
                      </select>
                    </div>

                    <label className="flex items-center gap-2 text-sm text-gray-200">
                      <input
                        type="checkbox"
                        checked={upNextTransfer}
                        onChange={(e) => setUpNextTransfer(e.target.checked)}
                        className="h-4 w-4 rounded border-gray-700 bg-transparent text-brand-500 focus:ring-brand-500"
                      />
                      This is an internal transfer between my own accounts
                    </label>

                    {upNextError && <p className="text-sm text-red-600">{upNextError}</p>}

                    <button
                      type="submit"
                      disabled={savingUpNext || (!upNextTransfer && !upNextCategoryId)}
                      className="rounded-lg bg-brand-500 px-4 py-2 text-sm font-medium text-white transition-colors duration-200 hover:bg-brand-600 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
                    >
                      {savingUpNext ? "Saving…" : "Save"}
                    </button>
                  </form>
                ) : (
                  <p className="text-sm text-gray-400">
                    Nothing left to categorize — every transaction has been handled.
                  </p>
                )}
              </section>

              <section className="space-y-2">
                <h2 className="text-sm font-medium text-gray-200">Handled</h2>
                {handled.length === 0 ? (
                  <p className="text-sm text-gray-400">No transactions have been handled yet.</p>
                ) : (
                  <ul className="space-y-2">
                    {handled.map((item) => (
                      <li
                        key={item.id}
                        className="space-y-2 rounded-lg border border-gray-800 p-3 text-sm text-gray-200"
                      >
                        <div className="flex items-center justify-between">
                          <span>
                            {item.date} — {item.description}
                          </span>
                          <span className={item.amount < 0 ? "text-red-500" : "text-emerald-500"}>
                            {item.amount.toFixed(2)}
                          </span>
                        </div>
                        <p className="text-xs text-gray-400">
                          {item.bankName} — {item.accountNumber}
                        </p>

                        <div className="flex flex-wrap items-center gap-3">
                          <select
                            value={item.categoryId ?? ""}
                            disabled={item.isInternalTransfer || savingHandledId === item.id}
                            onChange={(e) =>
                              void handleUpdateHandled(item, { categoryId: e.target.value })
                            }
                            className="rounded-lg border border-gray-700 bg-gray-900 p-2 text-sm text-gray-200 [color-scheme:dark] focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500 disabled:opacity-50"
                          >
                            <option value="">Uncategorized</option>
                            {categories.map((category) => (
                              <option key={category.id} value={category.id}>
                                {category.name}
                              </option>
                            ))}
                          </select>

                          <label className="flex items-center gap-2 text-sm text-gray-200">
                            <input
                              type="checkbox"
                              checked={item.isInternalTransfer}
                              disabled={savingHandledId === item.id}
                              onChange={(e) =>
                                void handleUpdateHandled(item, {
                                  isInternalTransfer: e.target.checked,
                                })
                              }
                              className="h-4 w-4 rounded border-gray-700 bg-transparent text-brand-500 focus:ring-brand-500"
                            />
                            Internal transfer
                          </label>
                        </div>

                        {handledErrors[item.id] && (
                          <p className="text-sm text-red-600">{handledErrors[item.id]}</p>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
              </section>
            </>
          )}
        </div>
      </main>
    </>
  );
}
