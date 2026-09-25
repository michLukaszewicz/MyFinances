import { useState } from "react";
import { Link, useLoaderData } from "react-router";
import type { Route } from "./+types/home";
import { AppHeader } from "../components/AppHeader";
import { apiFetch } from "../lib/api";

const valueProps = [
  {
    title: "Import your statements",
    description: "Upload bank CSV exports from mBank, Revolut, and Erste.",
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

// Mirrors the backend's TransactionContracts.cs
interface Transaction {
  id: string;
  date: string;
  description: string;
  amount: number;
  categoryId: string | null;
}

interface TransactionListResponseDto {
  items: Transaction[];
  hasMore: boolean;
}

const TRANSACTIONS_PAGE_SIZE = 20;

function formatAmount(amount: number): string {
  return amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
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

  if (!user) {
    return (
      <>
        <AppHeader authenticated={false} />
        <main className="relative flex min-h-screen items-center justify-center overflow-hidden pb-4">
          <div
            className="pointer-events-none absolute inset-0 -z-20 bg-[length:200%_200%] opacity-60 animate-[gradient-pan_18s_ease-in-out_infinite]"
            style={{
              backgroundImage:
                "radial-gradient(circle at 20% 20%, var(--color-brand-900) 0%, transparent 45%), radial-gradient(circle at 80% 30%, var(--color-brand-800) 0%, transparent 40%), radial-gradient(circle at 50% 80%, var(--color-brand-900) 0%, transparent 45%)",
            }}
            aria-hidden="true"
          />
          <div
            className="pointer-events-none absolute -left-24 top-10 -z-10 h-72 w-72 rounded-full bg-brand-600 opacity-30 blur-3xl animate-[drift-slow_14s_ease-in-out_infinite]"
            aria-hidden="true"
          />
          <div
            className="pointer-events-none absolute -right-24 bottom-0 -z-10 h-80 w-80 rounded-full bg-brand-400 opacity-20 blur-3xl animate-[drift-slow-reverse_16s_ease-in-out_infinite]"
            aria-hidden="true"
          />
          <div className="max-w-lg w-full space-y-8 px-4 text-center">
            <h1 className="sr-only">MyFinances</h1>
            <p
              className="text-sm text-gray-400 animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "0ms" }}
            >
              Categorize your own spend and compare it to your own history — no
              budget required up front.
            </p>
            <div
              className="flex justify-center gap-4 text-sm animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "100ms" }}
            >
              <a
                href="/login"
                className="rounded-md bg-brand-500 px-4 py-2 font-medium text-white shadow-md shadow-brand-900/40 transition-all duration-200 hover:scale-105 hover:bg-brand-600 hover:shadow-lg hover:shadow-brand-600/40 active:scale-95 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
              >
                Log in
              </a>
              <a
                href="/register"
                className="rounded-md px-4 py-2 font-medium text-brand-400 ring-1 ring-inset ring-brand-400 transition-all duration-200 hover:scale-105 hover:bg-brand-900/30 hover:shadow-lg hover:shadow-brand-600/20 active:scale-95 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
              >
                Register
              </a>
            </div>
            <div
              className="grid grid-cols-1 gap-4 pt-4 text-left sm:grid-cols-3 animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "200ms" }}
            >
              {valueProps.map((item) => (
                <div
                  key={item.title}
                  className="space-y-1 rounded-lg border border-gray-800 p-3 transition-all duration-200 hover:-translate-y-1 hover:border-brand-500/60 hover:bg-brand-900/30 hover:shadow-lg hover:shadow-brand-900/30"
                >
                  <h2 className="text-sm font-semibold text-gray-200">
                    {item.title}
                  </h2>
                  <p className="text-xs text-gray-400">{item.description}</p>
                </div>
              ))}
            </div>
          </div>
        </main>
      </>
    );
  }

  const hasTransactions = items.length > 0;

  async function handleLoadMore() {
    setLoadMoreError(null);
    setLoadingMore(true);
    try {
      const response = await apiFetch<TransactionListResponseDto>(
        `/transactions?skip=${items.length}&take=${TRANSACTIONS_PAGE_SIZE}`,
      );
      setItems((prev) => [...prev, ...response.items]);
      setHasMore(response.hasMore);
    } catch {
      setLoadMoreError("Something went wrong loading more transactions. Please try again.");
    } finally {
      setLoadingMore(false);
    }
  }

  return (
    <>
      <AppHeader authenticated />
      <main className="relative flex items-center justify-center overflow-hidden pt-16 pb-4">
        <div
          className="pointer-events-none absolute left-1/2 top-16 -z-10 h-56 w-56 -translate-x-1/2 rounded-full bg-brand-600 opacity-20 blur-3xl animate-[ambient-glow_6s_ease-in-out_infinite]"
          aria-hidden="true"
        />
        <div
          className={`w-full space-y-6 px-4 text-center ${hasTransactions ? "max-w-2xl" : "max-w-[300px]"}`}
        >
          <h1
            className="text-lg font-semibold text-gray-200 animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "0ms" }}
          >
            Welcome back, {user.email}
          </h1>

          {hasTransactions ? (
            <div
              className="space-y-4 text-left animate-[fade-slide-in_600ms_ease-out_both]"
              style={{ animationDelay: "100ms" }}
            >
              <ul className="space-y-2">
                {items.map((transaction) => (
                  <li
                    key={transaction.id}
                    className="flex items-center justify-between gap-3 rounded-lg border border-gray-800 p-3 text-sm text-gray-200"
                  >
                    <span className="shrink-0 text-gray-400">{transaction.date}</span>
                    <span className="flex-1 truncate px-3">{transaction.description}</span>
                    <span className="shrink-0 text-xs text-gray-500">
                      {transaction.categoryId === null ? "Uncategorized" : transaction.categoryId}
                    </span>
                    <span
                      className={`shrink-0 font-medium ${
                        transaction.amount < 0 ? "text-red-500" : "text-emerald-500"
                      }`}
                    >
                      {formatAmount(transaction.amount)}
                    </span>
                  </li>
                ))}
              </ul>

              {loadMoreError && <p className="text-sm text-red-600">{loadMoreError}</p>}

              {hasMore && (
                <div className="text-center">
                  <button
                    type="button"
                    onClick={handleLoadMore}
                    disabled={loadingMore}
                    className="rounded-md border border-gray-700 px-4 py-2 text-sm font-medium text-gray-200 transition-colors duration-200 hover:bg-gray-800 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
                  >
                    {loadingMore ? "Loading…" : "Load more"}
                  </button>
                </div>
              )}
            </div>
          ) : (
            <>
              <p
                className="text-sm text-gray-500 animate-[fade-slide-in_600ms_ease-out_both]"
                style={{ animationDelay: "100ms" }}
              >
                You haven't imported any transactions yet — let's start!
              </p>
              <Link
                to="/import"
                className="inline-block rounded-md bg-brand-500 px-4 py-2 text-sm font-medium text-white shadow-md shadow-brand-900/40 transition-all duration-200 hover:scale-105 hover:bg-brand-600 hover:shadow-lg hover:shadow-brand-600/40 active:scale-95 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500 animate-[fade-slide-in_600ms_ease-out_both]"
                style={{ animationDelay: "200ms" }}
              >
                Import a bank statement
              </Link>
            </>
          )}
        </div>
      </main>
    </>
  );
}
