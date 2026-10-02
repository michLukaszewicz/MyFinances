import { useEffect, useMemo, useState } from "react";
import { CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { apiFetch } from "../lib/api";
import type { CategoryDto } from "../lib/categories";
import { toDateInputValue } from "../lib/period";

// Mirrors the backend's CategoryTrendEndpoints.cs DTOs.
interface TrendBucketDto {
  start: string;
  end: string;
}

interface TrendSeriesDto {
  categoryId: string;
  categoryName: string;
  amounts: number[];
  total: number;
}

interface CategoryTrendDto {
  buckets: TrendBucketDto[];
  series: TrendSeriesDto[];
}

type Granularity = "week" | "month" | "year";
type Kind = "spend" | "income";

const MAX_BUCKETS = 120;
const DEFAULT_SELECTED = 5;

const GRANULARITY_LABELS: Record<Granularity, string> = { week: "Week", month: "Month", year: "Year" };

// Same palette as CategorySpendDonut so a category keeps its colour across both charts.
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

function parseDate(value: string): Date {
  const [y, m, d] = value.split("-").map(Number);
  return new Date(y, m - 1, d);
}

// 12 weeks / 12 months / 5 years ending today.
function defaultRange(granularity: Granularity): { from: string; to: string } {
  const now = new Date();
  const to = toDateInputValue(now);
  switch (granularity) {
    case "week":
      return { from: toDateInputValue(new Date(now.getFullYear(), now.getMonth(), now.getDate() - 83)), to };
    case "month":
      return { from: toDateInputValue(new Date(now.getFullYear(), now.getMonth() - 11, 1)), to };
    case "year":
      return { from: toDateInputValue(new Date(now.getFullYear() - 4, 0, 1)), to };
  }
}

// Mirrors the backend's bucketing (Monday-start weeks, calendar months/years) to size a range before fetching.
function bucketCount(granularity: Granularity, from: string, to: string): number {
  const start = parseDate(from);
  const end = parseDate(to);
  switch (granularity) {
    case "week": {
      const monday = new Date(start.getFullYear(), start.getMonth(), start.getDate() - ((start.getDay() + 6) % 7));
      const days = Math.round((end.getTime() - monday.getTime()) / 86_400_000);
      return Math.floor(days / 7) + 1;
    }
    case "month":
      return (end.getFullYear() - start.getFullYear()) * 12 + end.getMonth() - start.getMonth() + 1;
    case "year":
      return end.getFullYear() - start.getFullYear() + 1;
  }
}

function bucketLabel(granularity: Granularity, bucket: TrendBucketDto): string {
  const start = parseDate(bucket.start);
  switch (granularity) {
    case "week":
      return bucket.start;
    case "month":
      return start.toLocaleDateString(undefined, { month: "short", year: "numeric" });
    case "year":
      return String(start.getFullYear());
  }
}

function formatAmount(amount: number): string {
  return amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

const controlClass =
  "rounded-lg border border-gray-700 bg-gray-900 p-2 text-sm text-gray-200 [color-scheme:dark] focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500";

function toggleClass(active: boolean): string {
  return `rounded-md px-3 py-1 text-sm font-medium transition-colors ${
    active ? "bg-brand-600 text-white" : "text-gray-300 hover:bg-white/5"
  }`;
}

export function CategoryTrendChart() {
  const [granularity, setGranularity] = useState<Granularity>("month");
  const [range, setRange] = useState(() => defaultRange("month"));
  const [kind, setKind] = useState<Kind>("spend");
  const [data, setData] = useState<CategoryTrendDto | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [categories, setCategories] = useState<CategoryDto[]>([]);

  const today = toDateInputValue(new Date());

  let rangeError: string | null = null;
  if (!range.from || !range.to) {
    rangeError = "Pick both a start and an end date.";
  } else if (range.from > range.to) {
    rangeError = "The start date must be on or before the end date.";
  } else if (range.to > today) {
    rangeError = "The end date cannot be in the future.";
  } else if (bucketCount(granularity, range.from, range.to) > MAX_BUCKETS) {
    rangeError = `That range has more than ${MAX_BUCKETS} ${granularity}s. Choose a shorter range.`;
  }

  useEffect(() => {
    let cancelled = false;
    async function loadCategories() {
      try {
        const list = await apiFetch<CategoryDto[]>("/categorization/categories");
        if (!cancelled) setCategories(list);
      } catch {
        // Colours fall back to the series order; the chart itself does not depend on this list.
      }
    }
    void loadCategories();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    if (rangeError) return;
    let cancelled = false;
    async function load() {
      setLoading(true);
      try {
        const result = await apiFetch<CategoryTrendDto>(
          `/dashboard/category-trend?granularity=${granularity}&kind=${kind}&from=${range.from}&to=${range.to}`,
        );
        if (cancelled) return;
        setData(result);
        setLoadError(null);
        // A new kind, range or granularity starts from the highest-spend categories again.
        const top = [...result.series].sort((a, b) => b.total - a.total).slice(0, DEFAULT_SELECTED);
        setSelected(new Set(top.map((s) => s.categoryId)));
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
  }, [granularity, kind, range.from, range.to, rangeError]);

  function colorFor(categoryId: string, fallbackIndex: number): string {
    const index = categories.findIndex((c) => c.id === categoryId);
    return PALETTE[(index >= 0 ? index : fallbackIndex) % PALETTE.length];
  }

  const chartData = useMemo(() => {
    if (!data) return [];
    return data.buckets.map((bucket, i) => {
      const point: Record<string, string | number> = { label: bucketLabel(granularity, bucket) };
      for (const series of data.series) point[series.categoryId] = series.amounts[i];
      return point;
    });
  }, [data, granularity]);

  function changeGranularity(next: Granularity) {
    if (next === granularity) return;
    setGranularity(next);
    setRange(defaultRange(next));
  }

  function toggleCategory(categoryId: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(categoryId)) next.delete(categoryId);
      else next.add(categoryId);
      return next;
    });
  }

  const visibleSeries = data ? data.series.filter((s) => selected.has(s.categoryId)) : [];

  return (
    <section className="space-y-3 text-left" aria-labelledby="categoryTrendHeading">
      <h2 id="categoryTrendHeading" className="text-center text-sm font-medium text-gray-200">
        Category trend
      </h2>

      <div className="flex flex-wrap items-center justify-center gap-3">
        <div className="flex gap-1" role="group" aria-label="Granularity">
          {(Object.keys(GRANULARITY_LABELS) as Granularity[]).map((g) => (
            <button
              key={g}
              type="button"
              aria-pressed={granularity === g}
              onClick={() => changeGranularity(g)}
              className={toggleClass(granularity === g)}
            >
              {GRANULARITY_LABELS[g]}
            </button>
          ))}
        </div>
        <div className="flex gap-1" role="group" aria-label="Kind">
          {(["spend", "income"] as Kind[]).map((k) => (
            <button
              key={k}
              type="button"
              aria-pressed={kind === k}
              onClick={() => setKind(k)}
              className={toggleClass(kind === k)}
            >
              {k === "spend" ? "Spend" : "Income"}
            </button>
          ))}
        </div>
        <div className="flex items-center gap-2">
          <input
            type="date"
            aria-label="Trend from"
            value={range.from}
            max={range.to || today}
            onChange={(e) => setRange((r) => ({ ...r, from: e.target.value }))}
            className={controlClass}
          />
          <span className="text-sm text-gray-400">–</span>
          <input
            type="date"
            aria-label="Trend to"
            value={range.to}
            min={range.from || undefined}
            max={today}
            onChange={(e) => setRange((r) => ({ ...r, to: e.target.value }))}
            className={controlClass}
          />
        </div>
      </div>

      {rangeError && <p className="text-center text-sm text-red-400">{rangeError}</p>}
      {!rangeError && loadError && <p className="text-center text-sm text-red-600">{loadError}</p>}
      {!rangeError && !loadError && loading && <p className="text-center text-sm text-gray-400">Loading…</p>}

      {!rangeError && !loadError && !loading && data && data.series.length === 0 && (
        <p className="text-center text-sm text-gray-400">No categorized {kind} in this range.</p>
      )}

      {!rangeError && !loadError && !loading && data && data.series.length > 0 && (
        <>
          <ul className="flex flex-wrap justify-center gap-x-4 gap-y-1">
            {data.series.map((series, index) => (
              <li key={series.categoryId}>
                <label className="flex cursor-pointer items-center gap-2 text-sm text-gray-200">
                  <input
                    type="checkbox"
                    checked={selected.has(series.categoryId)}
                    onChange={() => toggleCategory(series.categoryId)}
                  />
                  <span
                    className="h-3 w-3 shrink-0 rounded-full"
                    style={{ backgroundColor: colorFor(series.categoryId, index) }}
                  />
                  {series.categoryName}
                </label>
              </li>
            ))}
          </ul>
          <div className="h-72 w-full">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={chartData}>
                <CartesianGrid stroke="#374151" strokeDasharray="3 3" />
                <XAxis dataKey="label" stroke="#9ca3af" tick={{ fontSize: 12 }} />
                <YAxis stroke="#9ca3af" tick={{ fontSize: 12 }} />
                <Tooltip
                  formatter={(value) => formatAmount(Number(value))}
                  contentStyle={{ backgroundColor: "#111827", border: "1px solid #374151", borderRadius: 8 }}
                  itemStyle={{ color: "#e5e7eb" }}
                  labelStyle={{ color: "#e5e7eb" }}
                />
                <Legend formatter={(value) => <span style={{ color: "#e5e7eb" }}>{value}</span>} />
                {visibleSeries.map((series) => (
                  <Line
                    key={series.categoryId}
                    type="monotone"
                    dataKey={series.categoryId}
                    name={series.categoryName}
                    stroke={colorFor(series.categoryId, data.series.indexOf(series))}
                    strokeWidth={2}
                    dot={chartData.length <= 24}
                    isAnimationActive={false}
                  />
                ))}
              </LineChart>
            </ResponsiveContainer>
          </div>
        </>
      )}
    </section>
  );
}
