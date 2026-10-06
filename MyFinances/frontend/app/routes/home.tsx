import { useEffect, useRef, useState } from "react";
import { Link, useLoaderData } from "react-router";
import type { Route } from "./+types/home";
import { AppHeader } from "../components/AppHeader";
import { CategorySpendDonut, type FlowKind } from "../components/CategorySpendDonut";
import { CategoryTrendChart } from "../components/CategoryTrendChart";
import { Button } from "../components/ui/button";
import { Card } from "../components/ui/card";
import { Input, selectClassName } from "../components/ui/input";
import { apiFetch, ApiError } from "../lib/api";
import { categoriesForAmount, type CategoryDto } from "../lib/categories";
import { cn } from "../lib/utils";
import {
  PRESET_LABELS,
  defaultSelection,
  periodQuery,
  resolvePeriod,
  toDateInputValue,
  toMonthInputValue,
  type Period,
  type PeriodPreset,
  type PeriodSelection,
} from "../lib/period";

const valueProps = [
  {
    title: "Import your statements",
    description: "Upload bank statements: CSV exports from mBank, Revolut, and Erste, or PDF statements from mBank, Erste, and VeloBank.",
  },
  {
    title: "Categorize in minutes",
    description: "Tag transactions and let duplicates and transfers surface themselves.",
  },
  {
    title: "See spend vs. your own history",
    description: "Compare each category against your own past averages, not a stranger's budget.",
  },
];

export function meta({}: Route.MetaArgs) {
  return [
    { title: "MyFinances" },
    {
      name: "description",
      content:
        "Categorize your own spend and compare it to your own history — no budget required up front.",
    },
  ];
}

// Mirrors the backend's TransactionContracts.cs TransactionListItemDto.
interface Transaction {
  id: string;
  date: string;
  description: string;
  amount: number;
  categoryId: string | null;
  categoryName: string | null;
  accountId: string;
  isInternalTransfer: boolean;
}

interface TransactionListResponseDto {
  items: Transaction[];
  hasMore: boolean;
}

// Mirrors the backend's TransactionContracts.cs TransactionDetailDto.
interface TransactionDetailDto {
  id: string;
  date: string;
  description: string;
  amount: number;
  accountId: string;
  categoryId: string;
  categoryName: string;
}

// Mirrors the backend's AccountContracts.cs AccountDto.
interface AccountDto {
  id: string;
  bankName: string;
  accountNumber: string;
}

interface ExistingTransaction {
  date: string;
  description: string;
  amount: number;
}

const TRANSACTIONS_PAGE_SIZE = 20;

function formatAmount(amount: number): string {
  return amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

// Local calendar date (not UTC) in the yyyy-MM-dd shape the <input type="date"> / backend expect.
function todayDateInputValue(): string {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, "0");
  const day = String(now.getDate()).padStart(2, "0");
  return `${now.getFullYear()}-${month}-${day}`;
}

// Query-string suffix restricting the transaction list to one category in the chosen period.
function categoryFilterQuery(categoryId: string | null, kind: FlowKind, period: Period): string {
  return categoryId
    ? `&categoryId=${encodeURIComponent(categoryId)}&${periodQuery(period)}&kind=${kind}`
    : "";
}

async function parseErrorBody(
  error: unknown,
): Promise<{ message: string; existingTransaction?: ExistingTransaction }> {
  if (error instanceof ApiError) {
    try {
      const body = await error.response.json();
      if (error.response.status === 409 && body?.existingTransaction) {
        return {
          message: typeof body?.title === "string" ? body.title : "A matching transaction already exists.",
          existingTransaction: body.existingTransaction,
        };
      }
      if (typeof body?.title === "string") {
        return { message: body.title };
      }
    } catch {
      // fall through to generic message
    }
  }
  return { message: "Something went wrong. Please try again." };
}

export async function clientLoader() {
  try {
    const res = await fetch("/api/auth/me", { credentials: "include" });
    if (!res.ok) return { user: null, transactions: null };
    const user = await res.json();

    // A transaction-fetch failure shouldn't break login state — caught and returned
    // as null distinctly from `user`, so the component below turns it into an inline
    // error message rather than crashing.
    let transactions: TransactionListResponseDto | null = null;
    try {
      transactions = await apiFetch<TransactionListResponseDto>(
        `/transactions?skip=0&take=${TRANSACTIONS_PAGE_SIZE}`,
      );
    } catch {
      transactions = null;
    }

    return { user, transactions };
  } catch {
    return { user: null, transactions: null };
  }
}

export default function Home() {
  const { user, transactions: initialTransactions } = useLoaderData<typeof clientLoader>();
  const [items, setItems] = useState<Transaction[]>(initialTransactions?.items ?? []);
  const [hasMore, setHasMore] = useState(initialTransactions?.hasMore ?? false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [loadMoreError, setLoadMoreError] = useState<string | null>(null);

  const [accounts, setAccounts] = useState<AccountDto[]>([]);
  const [categories, setCategories] = useState<CategoryDto[]>([]);

  const [isFormOpen, setIsFormOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingOriginalTransfer, setEditingOriginalTransfer] = useState(false);
  const [isInternalTransfer, setIsInternalTransfer] = useState(false);
  const [date, setDate] = useState("");
  const [description, setDescription] = useState("");
  const [amount, setAmount] = useState("");
  const [accountId, setAccountId] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [pendingDuplicate, setPendingDuplicate] = useState<ExistingTransaction | null>(null);

  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null);

  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);
  const [selectedCategoryName, setSelectedCategoryName] = useState<string | null>(null);
  const [selectedKind, setSelectedKind] = useState<FlowKind>("spend");
  // Draft is what the selector controls show; `period` is the last valid resolution and drives the
  // charts and the slice list, so a half-typed custom range never triggers a request.
  const [periodSelection, setPeriodSelection] = useState<PeriodSelection>(() => defaultSelection());
  const [period, setPeriod] = useState<Period>(() => resolvePeriod(defaultSelection())!);
  const [filterLoading, setFilterLoading] = useState(false);
  const isFirstFilterRun = useRef(true);
  // Bumped on every filter change so in-flight load-more/refresh responses fetched under the
  // previous filter are discarded instead of landing on the new list.
  const listGeneration = useRef(0);
  const [chartRefreshKey, setChartRefreshKey] = useState(0);

  // Deliberately not keyed on `period`: a period change always clears the slice selection in the same
  // batch (handlePeriodSelectionChange), and without a selection the list is unfiltered and period-independent.
  // If the selection is ever kept across period changes, add `period` to the dependencies below.
  // Re-fetch page 1 when the donut selection changes. The first run is skipped: the
  // clientLoader already provided the unfiltered page 1.
  useEffect(() => {
    listGeneration.current += 1;
    if (isFirstFilterRun.current) {
      isFirstFilterRun.current = false;
      return;
    }
    if (!user) return;
    let cancelled = false;
    async function loadFiltered() {
      setLoadMoreError(null);
      setFilterLoading(true);
      try {
        const response = await apiFetch<TransactionListResponseDto>(
          `/transactions?skip=0&take=${TRANSACTIONS_PAGE_SIZE}${categoryFilterQuery(selectedCategoryId, selectedKind, period)}`,
        );
        if (cancelled) return;
        setItems(response.items);
        setHasMore(response.hasMore);
      } catch {
        if (!cancelled) {
          // Don't leave the previous view's rows sitting under the new filter.
          setItems([]);
          setHasMore(false);
          setLoadMoreError("Something went wrong loading transactions. Please try again.");
        }
      } finally {
        if (!cancelled) setFilterLoading(false);
      }
    }
    void loadFiltered();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedCategoryId, selectedKind]);

  useEffect(() => {
    if (!user) return;
    async function loadOptions() {
      try {
        const [accountList, categoryList] = await Promise.all([
          apiFetch<AccountDto[]>("/accounts/"),
          apiFetch<CategoryDto[]>("/categorization/categories"),
        ]);
        setAccounts(accountList);
        setCategories(categoryList);
      } catch {
        // Options failure surfaces when the user tries to open the form and finds
        // empty selects; the transaction list itself still renders fine.
      }
    }
    void loadOptions();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user]);

  if (!user) {
    return (
      <>
        <AppHeader authenticated={false} />
        <main className="relative flex min-h-screen items-center justify-center overflow-hidden pb-4">
          <div
            className="pointer-events-none absolute inset-0 -z-20 bg-[length:200%_200%] opacity-20 animate-[gradient-pan_18s_ease-in-out_infinite]"
            style={{
              backgroundImage:
                "radial-gradient(circle at 20% 20%, var(--color-primary) 0%, transparent 45%), radial-gradient(circle at 80% 30%, var(--color-secondary) 0%, transparent 40%), radial-gradient(circle at 50% 80%, var(--color-primary) 0%, transparent 45%)",
            }}
            aria-hidden="true"
          />
          <div
            className="pointer-events-none absolute -left-24 top-10 -z-10 h-72 w-72 rounded-full bg-primary opacity-20 blur-3xl animate-[drift-slow_14s_ease-in-out_infinite]"
            aria-hidden="true"
          />
          <div
            className="pointer-events-none absolute -right-24 bottom-0 -z-10 h-80 w-80 rounded-full bg-secondary opacity-20 blur-3xl animate-[drift-slow-reverse_16s_ease-in-out_infinite]"
            aria-hidden="true"
          />
          <div className="max-w-lg w-full space-y-8 px-4 text-center">
            <h1 className="sr-only">MyFinances</h1>
            <p
              className="text-sm text-muted-foreground animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "0ms" }}
            >
              Categorize your own spend and compare it to your own history — no
              budget required up front.
            </p>
            <div
              className="flex justify-center gap-4 text-sm animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "100ms" }}
            >
              <Button
                asChild
                className="shadow-md transition-all duration-200 hover:scale-105 hover:shadow-lg active:scale-95"
              >
                <a href="/login">Log in</a>
              </Button>
              <Button
                asChild
                variant="outline"
                className="transition-all duration-200 hover:scale-105 hover:shadow-lg active:scale-95"
              >
                <a href="/register">Register</a>
              </Button>
            </div>
            <div
              className="grid grid-cols-1 gap-4 pt-4 text-left sm:grid-cols-3 animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "200ms" }}
            >
              {valueProps.map((item) => (
                <Card
                  key={item.title}
                  className="gap-1 p-3 transition-all duration-200 hover:-translate-y-1 hover:border-primary/60 hover:bg-accent hover:shadow-lg"
                >
                  <h2 className="text-sm font-semibold text-foreground">
                    {item.title}
                  </h2>
                  <p className="text-xs text-muted-foreground">{item.description}</p>
                </Card>
              ))}
            </div>
          </div>
        </main>
      </>
    );
  }

  // A filter that matches nothing must still show the list branch (with the Clear affordance).
  const hasTransactions = items.length > 0 || selectedCategoryId !== null;

  function handleSelectCategory(id: string | null, name: string | null, kind: FlowKind = "spend") {
    setSelectedCategoryId(id);
    setSelectedCategoryName(name);
    setSelectedKind(kind);
  }

  function handlePeriodSelectionChange(next: PeriodSelection) {
    setPeriodSelection(next);
    const resolved = resolvePeriod(next);
    if (resolved && (resolved.from !== period.from || resolved.to !== period.to)) {
      setPeriod(resolved);
      // The slice list is scoped to the period, so an active slice selection no longer applies.
      handleSelectCategory(null, null);
    }
  }

  function handlePresetChange(preset: PeriodPreset) {
    handlePeriodSelectionChange({ ...periodSelection, preset });
  }

  const todayValue = toDateInputValue(new Date());
  const periodInvalid = resolvePeriod(periodSelection) === null;
  const periodInvalidMessage =
    periodSelection.preset === "custom"
      ? "Pick a start date on or before the end date, neither in the future."
      : "Pick a month that is not in the future.";

  async function handleLoadMore() {
    setLoadMoreError(null);
    setLoadingMore(true);
    const generation = listGeneration.current;
    try {
      const response = await apiFetch<TransactionListResponseDto>(
        `/transactions?skip=${items.length}&take=${TRANSACTIONS_PAGE_SIZE}${categoryFilterQuery(selectedCategoryId, selectedKind, period)}`,
      );
      if (generation !== listGeneration.current) return;
      setItems((prev) => [...prev, ...response.items]);
      setHasMore(response.hasMore);
    } catch {
      setLoadMoreError("Something went wrong loading more transactions. Please try again.");
    } finally {
      setLoadingMore(false);
    }
  }

  async function refreshTransactions() {
    setChartRefreshKey((key) => key + 1);
    const generation = listGeneration.current;
    try {
      const take = Math.max(items.length, TRANSACTIONS_PAGE_SIZE);
      const response = await apiFetch<TransactionListResponseDto>(
        `/transactions?skip=0&take=${take}${categoryFilterQuery(selectedCategoryId, selectedKind, period)}`,
      );
      if (generation !== listGeneration.current) return;
      setItems(response.items);
      setHasMore(response.hasMore);
    } catch {
      // Leave the current list in place — the write itself already succeeded.
    }
  }

  function closeForm() {
    setIsFormOpen(false);
    setEditingId(null);
    setEditingOriginalTransfer(false);
    setIsInternalTransfer(false);
    setDate("");
    setDescription("");
    setAmount("");
    setAccountId("");
    setCategoryId("");
    setFormError(null);
    setPendingDuplicate(null);
  }

  function startAdd() {
    setEditingId(null);
    setEditingOriginalTransfer(false);
    setIsInternalTransfer(false);
    setDate(todayDateInputValue());
    setDescription("");
    setAmount("");
    setAccountId("");
    setCategoryId("");
    setFormError(null);
    setPendingDuplicate(null);
    setIsFormOpen(true);
  }

  function startEdit(transaction: Transaction) {
    setEditingId(transaction.id);
    setEditingOriginalTransfer(transaction.isInternalTransfer);
    setIsInternalTransfer(transaction.isInternalTransfer);
    setDate(transaction.date);
    setDescription(transaction.description);
    setAmount(String(transaction.amount));
    setAccountId(transaction.accountId);
    setCategoryId(transaction.categoryId ?? "");
    setFormError(null);
    setPendingDuplicate(null);
    setIsFormOpen(true);
  }

  async function submitTransaction(force: boolean) {
    const amountValue = Number(amount);
    // An internal transfer may legitimately have no category; in that case only the flag is saved.
    const flagOnly = editingId !== null && isInternalTransfer && !categoryId;
    if (!flagOnly && (!date || !description || !accountId || !categoryId || Number.isNaN(amountValue))) return;

    setFormError(null);
    setSubmitting(true);
    try {
      const body = JSON.stringify({
        Date: date,
        Description: description,
        Amount: amountValue,
        AccountId: accountId,
        CategoryId: categoryId,
        Force: force,
      });

      if (editingId) {
        if (!flagOnly) {
          await apiFetch<TransactionDetailDto>(`/transactions/${editingId}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body,
          });
        }
        if (isInternalTransfer !== editingOriginalTransfer) {
          await apiFetch(`/categorization/transactions/${editingId}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ IsInternalTransfer: isInternalTransfer }),
          });
        }
      } else {
        await apiFetch<TransactionDetailDto>("/transactions", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body,
        });
      }
      await refreshTransactions();
      closeForm();
    } catch (err) {
      const { message, existingTransaction } = await parseErrorBody(err);
      setFormError(message);
      setPendingDuplicate(existingTransaction ?? null);
    } finally {
      setSubmitting(false);
    }
  }

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void submitTransaction(false);
  }

  function handleForceSubmit() {
    void submitTransaction(true);
  }

  async function handleDelete(id: string) {
    try {
      await apiFetch(`/transactions/${id}`, { method: "DELETE" });
      setConfirmingDeleteId(null);
      await refreshTransactions();
    } catch {
      setConfirmingDeleteId(null);
    }
  }

  const transactionForm = isFormOpen ? (
    accounts.length === 0 ? (
      <Card className="gap-3 p-4 text-left">
        <p className="text-sm text-muted-foreground">
          You need at least one account before adding a transaction. Add one in{" "}
          <Link
            to="/settings"
            className="rounded-sm text-primary outline-none hover:text-primary/80 focus-visible:ring-2 focus-visible:ring-ring"
          >
            Settings
          </Link>
          .
        </p>
        <div>
          <Button type="button" variant="outline" onClick={closeForm}>
            Close
          </Button>
        </div>
      </Card>
    ) : (
      <Card className="gap-4 p-4 text-left">
        <form onSubmit={handleSubmit} className="space-y-4">
          <h2 className="text-sm font-medium text-foreground">
            {editingId ? "Edit transaction" : "Add transaction"}
          </h2>

          <div className="space-y-1">
            <label htmlFor="transactionDate" className="text-sm text-foreground">
              Date
            </label>
            <Input
              id="transactionDate"
              type="date"
              required
              value={date}
              onChange={(e) => setDate(e.target.value)}
            />
          </div>

          <div className="space-y-1">
            <label htmlFor="transactionDescription" className="text-sm text-foreground">
              Description
            </label>
            <Input
              id="transactionDescription"
              type="text"
              required
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
          </div>

          <div className="space-y-1">
            <label htmlFor="transactionAmount" className="text-sm text-foreground">
              Amount
            </label>
            <Input
              id="transactionAmount"
              type="number"
              step="0.01"
              required
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
          </div>

          <div className="space-y-1">
            <label htmlFor="transactionAccount" className="text-sm text-foreground">
              Account
            </label>
            <select
              id="transactionAccount"
              required
              value={accountId}
              onChange={(e) => setAccountId(e.target.value)}
              className={selectClassName}
            >
              <option value="">Select an account…</option>
              {accounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.bankName} — {account.accountNumber}
                </option>
              ))}
            </select>
          </div>

          <div className="space-y-1">
            <label htmlFor="transactionCategory" className="text-sm text-foreground">
              Category
            </label>
            <select
              id="transactionCategory"
              required={!(editingId && isInternalTransfer)}
              value={categoryId}
              onChange={(e) => setCategoryId(e.target.value)}
              className={selectClassName}
            >
              <option value="">Select a category…</option>
              {categoriesForAmount(categories, amount.trim() === "" ? null : Number(amount.replace(",", ".")), categoryId).map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </select>
          </div>

          {editingId && (
            <label className="flex items-center gap-2 text-sm text-foreground">
              <Input
                type="checkbox"
                checked={isInternalTransfer}
                onChange={(e) => setIsInternalTransfer(e.target.checked)}
                className="h-4 w-4 shrink-0 p-0 accent-primary"
              />
              This is an internal transfer between my own accounts
            </label>
          )}

          {formError && <p className="text-sm text-destructive">{formError}</p>}

          {pendingDuplicate && (
            <div className="space-y-2 rounded-lg border border-warning/60 bg-warning/10 p-3 text-sm text-warning">
              <p>
                A similar transaction already exists: {pendingDuplicate.date} —{" "}
                {pendingDuplicate.description} — {formatAmount(pendingDuplicate.amount)}
              </p>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={handleForceSubmit}
                disabled={submitting}
                className="border-warning text-warning hover:bg-warning/20 hover:text-warning"
              >
                Save anyway
              </Button>
            </div>
          )}

          <div className="flex gap-2">
            <Button type="submit" disabled={submitting}>
              {submitting ? "Saving…" : editingId ? "Save changes" : "Add transaction"}
            </Button>
            <Button type="button" variant="ghost" onClick={closeForm}>
              Cancel
            </Button>
          </div>
        </form>
      </Card>
    )
  ) : (
    <Button type="button" variant="outline" onClick={startAdd} className="w-full">
      Add transaction
    </Button>
  );

  return (
    <>
      <AppHeader authenticated />
      <main className="relative flex items-center justify-center overflow-hidden pt-16 pb-4">
        <div
          className="pointer-events-none absolute left-1/2 top-16 -z-10 h-56 w-56 -translate-x-1/2 rounded-full bg-primary opacity-20 blur-3xl animate-[ambient-glow_6s_ease-in-out_infinite]"
          aria-hidden="true"
        />
        <div
          className="w-full max-w-4xl space-y-6 px-4 text-center"
        >
          <h1
            className="text-lg font-semibold text-foreground animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "0ms" }}
          >
            Welcome back, {user.email}
          </h1>

          <div className="flex flex-wrap items-center justify-center gap-2">
            <label htmlFor="periodPreset" className="text-sm text-muted-foreground">
              Period
            </label>
            <select
              id="periodPreset"
              value={periodSelection.preset}
              onChange={(e) => handlePresetChange(e.target.value as PeriodPreset)}
              className={cn(selectClassName, "w-auto")}
            >
              {(Object.keys(PRESET_LABELS) as PeriodPreset[]).map((preset) => (
                <option key={preset} value={preset}>
                  {PRESET_LABELS[preset]}
                </option>
              ))}
            </select>
            {periodSelection.preset === "month" && (
              <Input
                type="month"
                aria-label="Month"
                value={periodSelection.month}
                max={toMonthInputValue(new Date())}
                onChange={(e) => handlePeriodSelectionChange({ ...periodSelection, month: e.target.value })}
                className="w-auto"
              />
            )}
            {periodSelection.preset === "custom" && (
              <>
                <Input
                  type="date"
                  aria-label="From"
                  value={periodSelection.customFrom}
                  max={periodSelection.customTo || todayValue}
                  onChange={(e) => handlePeriodSelectionChange({ ...periodSelection, customFrom: e.target.value })}
                  className="w-auto"
                />
                <span className="text-sm text-muted-foreground">–</span>
                <Input
                  type="date"
                  aria-label="To"
                  value={periodSelection.customTo}
                  min={periodSelection.customFrom || undefined}
                  max={todayValue}
                  onChange={(e) => handlePeriodSelectionChange({ ...periodSelection, customTo: e.target.value })}
                  className="w-auto"
                />
              </>
            )}
          </div>
          {periodInvalid && <p className="text-center text-sm text-destructive">{periodInvalidMessage}</p>}

          <div className="grid grid-cols-1 gap-6 sm:grid-cols-2">
            <CategorySpendDonut
              kind="spend"
              period={period}
              selectedCategoryId={selectedKind === "spend" ? selectedCategoryId : null}
              refreshKey={chartRefreshKey}
              onSelectCategory={(id, name) => handleSelectCategory(id, name, "spend")}
            />
            <CategorySpendDonut
              kind="income"
              period={period}
              selectedCategoryId={selectedKind === "income" ? selectedCategoryId : null}
              refreshKey={chartRefreshKey}
              onSelectCategory={(id, name) => handleSelectCategory(id, name, "income")}
            />
          </div>

          <CategoryTrendChart />

          {hasTransactions ? (
            <div
              className="space-y-4 text-left animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "100ms" }}
            >
              {transactionForm}

              {selectedCategoryId && (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <span>
                    Filtering by: <span className="text-foreground">{selectedCategoryName}</span> ({selectedKind}) · {period.label}
                  </span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => handleSelectCategory(null, null)}
                  >
                    Clear
                  </Button>
                </div>
              )}

              {filterLoading && <p className="text-sm text-muted-foreground">Loading…</p>}

              {!filterLoading && items.length === 0 && (
                <p className="text-center text-sm text-muted-foreground">No transactions in this category for {period.label}.</p>
              )}

              <ul className="space-y-2">
                {items.map((transaction) => (
                  <li key={transaction.id}>
                    <Card className="flex-row items-center justify-between gap-3 p-3 text-sm text-foreground">
                      <span className="shrink-0 text-muted-foreground">{transaction.date}</span>
                      <span className="flex-1 truncate px-3">{transaction.description}</span>
                      <span className="shrink-0 text-xs text-muted-foreground">
                        {transaction.isInternalTransfer
                          ? "Internal transfer"
                          : (transaction.categoryName ?? "Uncategorized")}
                      </span>
                      <span
                        className={`shrink-0 font-medium ${
                          transaction.amount < 0 ? "text-destructive" : "text-success"
                        }`}
                      >
                        {formatAmount(transaction.amount)}
                      </span>
                      <div className="flex shrink-0 items-center gap-2">
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          onClick={() => startEdit(transaction)}
                        >
                          Edit
                        </Button>
                        {confirmingDeleteId === transaction.id ? (
                          <>
                            <Button
                              type="button"
                              variant="destructive-ghost"
                              size="sm"
                              onClick={() => void handleDelete(transaction.id)}
                            >
                              Confirm delete
                            </Button>
                            <Button
                              type="button"
                              variant="ghost"
                              size="sm"
                              onClick={() => setConfirmingDeleteId(null)}
                              className="text-muted-foreground"
                            >
                              Cancel
                            </Button>
                          </>
                        ) : (
                          <Button
                            type="button"
                            variant="destructive-ghost"
                            size="sm"
                            onClick={() => setConfirmingDeleteId(transaction.id)}
                          >
                            Delete
                          </Button>
                        )}
                      </div>
                    </Card>
                  </li>
                ))}
              </ul>

              {loadMoreError && <p className="text-sm text-destructive">{loadMoreError}</p>}

              {hasMore && (
                <div className="text-center">
                  <Button
                    type="button"
                    variant="outline"
                    onClick={handleLoadMore}
                    disabled={loadingMore}
                  >
                    {loadingMore ? "Loading…" : "Load more"}
                  </Button>
                </div>
              )}
            </div>
          ) : (
            <div
              className="space-y-4 text-left animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "100ms" }}
            >
              {transactionForm}

              <p className="text-center text-sm text-muted-foreground">
                You haven't imported any transactions yet — let's start!
              </p>
              <div className="text-center">
                <Button asChild className="transition-all duration-200 hover:scale-105 active:scale-95">
                  <Link to="/import">Import a bank statement</Link>
                </Button>
              </div>
            </div>
          )}
        </div>
      </main>
    </>
  );
}
