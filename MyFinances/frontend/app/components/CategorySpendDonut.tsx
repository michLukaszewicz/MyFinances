import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { Legend, Pie, PieChart, ResponsiveContainer, Tooltip } from "recharts";
import { apiFetch } from "../lib/api";

// Mirrors the backend's DashboardContracts.cs CategorySpendDto.
interface CategorySpendDto {
  categoryId: string;
  categoryName: string;
  amount: number;
  // Only returned by the spend endpoint; null when there is no history to compare against.
  averageToDate?: number | null;
  deviation?: Deviation | null;
}

type Deviation = "above" | "below" | "inLine";

const DEVIATION_BADGE: Record<Deviation, { label: string; className: string }> = {
  above: { label: "Above average", className: "bg-red-900/40 text-red-300" },
  below: { label: "Below average", className: "bg-green-900/40 text-green-300" },
  inLine: { label: "In line", className: "bg-gray-700 text-gray-300" },
};

export type FlowKind = "spend" | "income";

const FLOW_COPY: Record<FlowKind, { endpoint: string; title: string; empty: string }> = {
  spend: {
    endpoint: "/dashboard/category-spend",
    title: "Spend by category — this month",
    empty: "No categorized spend for this month yet.",
  },
  income: {
    endpoint: "/dashboard/category-income",
    title: "Income by category — this month",
    empty: "No categorized income for this month yet.",
  },
};

interface CategorySpendDonutProps {
  kind: FlowKind;
  selectedCategoryId: string | null;
  // Bump to re-fetch the chart (e.g. after a transaction is added, edited or deleted).
  refreshKey: number;
  // categoryName is passed alongside the id so the parent can label the active filter.
  onSelectCategory: (categoryId: string | null, categoryName: string | null) => void;
}

// At least 12 entries (12 seeded categories); indexed by position modulo length.
const PALETTE = [
  "#6366f1",
  "#f59e0b",
  "#10b981",
  "#ef4444",
  "#3b82f6",
  "#ec4899",
  "#14b8a6",
  "#f97316",
  "#8b5cf6",
  "#84cc16",
  "#06b6d4",
  "#eab308",
];

function formatAmount(amount: number): string {
  return amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

export function CategorySpendDonut({ kind, selectedCategoryId, refreshKey, onSelectCategory }: CategorySpendDonutProps) {
  const copy = FLOW_COPY[kind];
  const [data, setData] = useState<CategorySpendDto[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  // Read inside the fetch effect without making a selection change re-fetch the chart.
  const selectedRef = useRef(selectedCategoryId);
  selectedRef.current = selectedCategoryId;

  useEffect(() => {
    let cancelled = false;
    async function load() {
      try {
        const result = await apiFetch<CategorySpendDto[]>(copy.endpoint);
        if (cancelled) return;
        setData(result);
        setLoadError(null);
        // A write can remove the selected category from this month's spend; drop the stale filter.
        if (selectedRef.current !== null && !result.some((e) => e.categoryId === selectedRef.current)) {
          onSelectCategory(null, null);
        }
      } catch {
        if (!cancelled) setLoadError("Something went wrong loading this chart. Please try again.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    void load();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [refreshKey]);

  if (loading) {
    return <p className="text-sm text-gray-400">Loading…</p>;
  }

  if (loadError) {
    return <p className="text-sm text-red-600">{loadError}</p>;
  }

  if (!data || data.length === 0) {
    return (
      <p className="text-center text-sm text-gray-400">
        {copy.empty}{" "}
        <Link to="/categorize" className="text-brand-400 hover:text-brand-300">
          Categorize transactions
        </Link>
      </p>
    );
  }

  const chartData = data.map((entry, index) => ({
    ...entry,
    fill: PALETTE[index % PALETTE.length],
    fillOpacity: selectedCategoryId === null || selectedCategoryId === entry.categoryId ? 1 : 0.35,
  }));

  function toggleEntry(entry: CategorySpendDto) {
    if (entry.categoryId === selectedCategoryId) {
      onSelectCategory(null, null);
    } else {
      onSelectCategory(entry.categoryId, entry.categoryName);
    }
  }

  function handleSliceClick(_slice: unknown, index: number) {
    const entry = data?.[index];
    if (!entry) return;
    toggleEntry(entry);
  }

  return (
    <div className="space-y-2">
      <h2 className="text-center text-sm font-medium text-gray-200">{copy.title}</h2>
      <div className="h-72 w-full">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={chartData}
              dataKey="amount"
              nameKey="categoryName"
              innerRadius="55%"
              outerRadius="80%"
              stroke="none"
              cursor="pointer"
              onClick={handleSliceClick}
            />
            <Tooltip
              formatter={(value) => formatAmount(Number(value))}
              contentStyle={{ backgroundColor: "#111827", border: "1px solid #374151", borderRadius: 8 }}
              itemStyle={{ color: "#e5e7eb" }}
              labelStyle={{ color: "#e5e7eb" }}
            />
            <Legend
              formatter={(value) => <span style={{ color: "#e5e7eb" }}>{value}</span>}
            />
          </PieChart>
        </ResponsiveContainer>
      </div>
      {kind === "spend" && (
        <ul className="space-y-1">
          {chartData.map((entry) => {
            const badge = entry.deviation ? DEVIATION_BADGE[entry.deviation] : null;
            return (
              <li key={entry.categoryId}>
                <button
                  type="button"
                  onClick={() => toggleEntry(entry)}
                  aria-pressed={selectedCategoryId === entry.categoryId}
                  style={{ opacity: entry.fillOpacity }}
                  className="flex w-full items-center gap-2 rounded px-2 py-1 text-left text-sm hover:bg-gray-800"
                >
                  <span className="h-3 w-3 shrink-0 rounded-full" style={{ backgroundColor: entry.fill }} />
                  <span className="min-w-0 flex-1 truncate text-gray-200">{entry.categoryName}</span>
                  <span className="text-gray-200">{formatAmount(entry.amount)}</span>
                  {badge && (
                    <span className="flex flex-col items-end">
                      <span className={`rounded px-2 py-0.5 text-xs font-medium ${badge.className}`}>{badge.label}</span>
                      {entry.averageToDate != null && (
                        <span className="text-xs text-gray-400">avg by this day: {formatAmount(entry.averageToDate)}</span>
                      )}
                    </span>
                  )}
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
