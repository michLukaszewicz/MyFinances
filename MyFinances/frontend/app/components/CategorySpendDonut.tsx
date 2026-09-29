import { useEffect, useState } from "react";
import { Link } from "react-router";
import { Legend, Pie, PieChart, ResponsiveContainer, Tooltip } from "recharts";
import { apiFetch } from "../lib/api";

// Mirrors the backend's DashboardContracts.cs CategorySpendDto.
interface CategorySpendDto {
  categoryId: string;
  categoryName: string;
  amount: number;
}

interface CategorySpendDonutProps {
  selectedCategoryId: string | null;
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

export function CategorySpendDonut({ selectedCategoryId, onSelectCategory }: CategorySpendDonutProps) {
  const [data, setData] = useState<CategorySpendDto[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      try {
        const result = await apiFetch<CategorySpendDto[]>("/dashboard/category-spend");
        if (!cancelled) setData(result);
      } catch {
        if (!cancelled) setLoadError("Something went wrong loading category spend. Please try again.");
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  if (loading) {
    return <p className="text-sm text-gray-400">Loading category spend…</p>;
  }

  if (loadError) {
    return <p className="text-sm text-red-600">{loadError}</p>;
  }

  if (!data || data.length === 0) {
    return (
      <p className="text-center text-sm text-gray-400">
        No categorized spend for this month yet.{" "}
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

  function handleSliceClick(_slice: unknown, index: number) {
    const entry = data?.[index];
    if (!entry) return;
    if (entry.categoryId === selectedCategoryId) {
      onSelectCategory(null, null);
    } else {
      onSelectCategory(entry.categoryId, entry.categoryName);
    }
  }

  return (
    <div className="space-y-2">
      <h2 className="text-center text-sm font-medium text-gray-200">Spend by category — this month</h2>
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
    </div>
  );
}
