import { useEffect, useState } from "react";
import { redirect } from "react-router";
import { apiFetch, ApiError } from "../lib/api";
import { categoriesForAmount, type CategoryDto } from "../lib/categories";
import { AppHeader } from "../components/AppHeader";
import { Button } from "../components/ui/button";
import { Card, CardContent } from "../components/ui/card";
import { selectClassName } from "../components/ui/input";

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

export default function Categorize() {
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [queue, setQueue] = useState<TransactionQueueItemDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [upNextCategoryId, setUpNextCategoryId] = useState("");
  const [upNextTransfer, setUpNextTransfer] = useState(false);
  const [upNextError, setUpNextError] = useState<string | null>(null);
  const [savingUpNext, setSavingUpNext] = useState(false);

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
    } catch (err) {
      setUpNextError(await extractErrorMessage(err));
    } finally {
      setSavingUpNext(false);
    }
  }

  return (
    <>
      <AppHeader authenticated />
      <main className="flex items-center justify-center pb-4">
        <div className="w-full max-w-2xl space-y-8 px-4">
          <h1 className="text-center text-lg font-semibold text-foreground">Categorize</h1>

          {loading ? (
            <p className="text-center text-sm text-muted-foreground">Loading…</p>
          ) : loadError ? (
            <p className="text-center text-sm text-destructive">{loadError}</p>
          ) : (
            <>
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
