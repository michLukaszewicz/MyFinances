import { useEffect, useMemo, useState } from "react";
import { CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { Button } from "~/components/ui/button";
import { Input } from "~/components/ui/input";
import { apiErrorMessage, apiFetch } from "../lib/api";
import type { CategoryDto } from "../lib/categories";
import { chartColor } from "../lib/chartPalette";
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

const dateInputClass = "w-auto [color-scheme:dark]";

export function CategoryTrendChart() {
  const [granularity, setGranularity] = useState<Granularity>("month");
  const [range, setRange] = useState(() => defaultRange("month"));
  const [kind, setKind] = useState<Kind>("spend");
  const [data, setData] = useState<CategoryTrendDto | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  // Colours come from the category list, so hold rendering until it has loaded (or failed) to keep them from shifting.
  const [categoriesSettled, setCategoriesSettled] = useState(false);

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
      } finally {
        if (!cancelled) setCategoriesSettled(true);
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
      setLoadError(null);
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
      } catch (err) {
        if (!cancelled) setLoadError(await apiErrorMessage(err, "Something went wrong loading this chart. Please try again."));
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
    return chartColor(index >= 0 ? index : fallbackIndex);
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
      <h2 id="categoryTrendHeading" className="text-center text-sm font-medium text-foreground">
        Category trend
      </h2>

      <div className="flex flex-wrap items-center justify-center gap-3">
        <div className="flex gap-1" role="group" aria-label="Granularity">
          {(Object.keys(GRANULARITY_LABELS) as Granularity[]).map((g) => (
            <Button
              key={g}
              type="button"
              size="sm"
              variant={granularity === g ? "default" : "ghost"}
              aria-pressed={granularity === g}
              onClick={() => changeGranularity(g)}
            >
              {GRANULARITY_LABELS[g]}
            </Button>
          ))}
        </div>
        <div className="flex gap-1" role="group" aria-label="Kind">
          {(["spend", "income"] as Kind[]).map((k) => (
            <Button
              key={k}
              type="button"
              size="sm"
              variant={kind === k ? "default" : "ghost"}
              aria-pressed={kind === k}
              onClick={() => setKind(k)}
            >
              {k === "spend" ? "Spend" : "Income"}
            </Button>
          ))}
        </div>
        <div className="flex items-center gap-2">
          <Input
            type="date"
            aria-label="Trend from"
            value={range.from}
            max={range.to || today}
            onChange={(e) => setRange((r) => ({ ...r, from: e.target.value }))}
            className={dateInputClass}
          />
          <span className="text-sm text-muted-foreground">–</span>
          <Input
            type="date"
            aria-label="Trend to"
            value={range.to}
            min={range.from || undefined}
            max={today}
            onChange={(e) => setRange((r) => ({ ...r, to: e.target.value }))}
            className={dateInputClass}
          />
        </div>
      </div>

      {rangeError && <p className="text-center text-sm text-destructive">{rangeError}</p>}
      {!rangeError && loadError && <p className="text-center text-sm text-destructive">{loadError}</p>}
      {!rangeError && !loadError && (loading || !categoriesSettled) && <p className="text-center text-sm text-muted-foreground">Loading…</p>}

      {!rangeError && !loadError && !loading && categoriesSettled && data && data.series.length === 0 && (
        <p className="text-center text-sm text-muted-foreground">No categorized {kind} in this range.</p>
      )}

      {!rangeError && !loadError && !loading && categoriesSettled && data && data.series.length > 0 && (
        <>
          <ul className="flex flex-wrap justify-center gap-x-4 gap-y-1">
            {data.series.map((series, index) => (
              <li key={series.categoryId}>
                <label className="flex cursor-pointer items-center gap-2 text-sm text-foreground">
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
                <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
                <XAxis dataKey="label" stroke="var(--muted-foreground)" tick={{ fontSize: 12 }} />
                <YAxis stroke="var(--muted-foreground)" tick={{ fontSize: 12 }} />
                <Tooltip
                  formatter={(value) => formatAmount(Number(value))}
                  contentStyle={{ backgroundColor: "var(--popover)", border: "1px solid var(--border)", borderRadius: 8 }}
                  itemStyle={{ color: "var(--popover-foreground)" }}
                  labelStyle={{ color: "var(--popover-foreground)" }}
                />
                <Legend formatter={(value) => <span style={{ color: "var(--foreground)" }}>{value}</span>} />
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
