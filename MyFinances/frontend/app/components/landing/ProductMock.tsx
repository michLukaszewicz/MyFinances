import { chartColor } from "../../lib/chartPalette";

type Deviation = "above" | "below" | "inLine";

const DEVIATION_BADGE: Record<Deviation, { label: string; className: string }> = {
  above: { label: "Above average", className: "bg-destructive/20 text-destructive" },
  below: { label: "Below average", className: "bg-success/20 text-success" },
  inLine: { label: "In line", className: "bg-muted text-muted-foreground" },
};

const SAMPLE_CATEGORIES: { name: string; amount: number; deviation: Deviation }[] = [
  { name: "Housing", amount: 2000, deviation: "inLine" },
  { name: "Groceries", amount: 800, deviation: "above" },
  { name: "Transport", amount: 400, deviation: "below" },
  { name: "Leisure", amount: 300, deviation: "above" },
  { name: "Other", amount: 100, deviation: "below" },
];

const TOTAL = SAMPLE_CATEGORIES.reduce((sum, category) => sum + category.amount, 0);
const RADIUS = 40;

function formatAmount(amount: number): string {
  return `${amount.toLocaleString("en-US")}.00 PLN`;
}

export function ProductMock() {
  let offset = 0;

  return (
    <div
      role="img"
      aria-label="Illustration of the dashboard: a spend-by-category donut chart and a category list with average-deviation badges, using example data"
      className="mx-auto w-full max-w-2xl animate-[float-slow_8s_ease-in-out_infinite]"
    >
      <div aria-hidden="true" className="rounded-xl border border-border bg-card p-4 text-left shadow-lg sm:p-6">
        <div className="mb-4 flex items-center justify-between gap-2">
          <p className="text-sm font-medium text-foreground">Spend by category</p>
          <span className="rounded bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">Example data</span>
        </div>
        <div className="flex flex-col items-center gap-6 sm:flex-row">
          <svg viewBox="0 0 100 100" className="h-40 w-40 shrink-0 -rotate-90">
            {SAMPLE_CATEGORIES.map((category, index) => {
              const share = (category.amount / TOTAL) * 100;
              const circle = (
                <circle
                  key={category.name}
                  cx="50"
                  cy="50"
                  r={RADIUS}
                  fill="none"
                  stroke={chartColor(index)}
                  strokeWidth="16"
                  pathLength={100}
                  strokeDasharray={`${share} ${100 - share}`}
                  strokeDashoffset={-offset}
                />
              );
              offset += share;
              return circle;
            })}
          </svg>
          <ul className="w-full min-w-0 space-y-2">
            {SAMPLE_CATEGORIES.map((category, index) => {
              const badge = DEVIATION_BADGE[category.deviation];
              return (
                <li key={category.name} className="flex items-center gap-2 text-sm">
                  <span className="h-3 w-3 shrink-0 rounded-full" style={{ backgroundColor: chartColor(index) }} />
                  <span className="min-w-0 flex-1 truncate text-foreground">{category.name}</span>
                  <span className="text-foreground">{formatAmount(category.amount)}</span>
                  <span className={`hidden rounded px-2 py-0.5 text-xs font-medium sm:inline ${badge.className}`}>
                    {badge.label}
                  </span>
                </li>
              );
            })}
          </ul>
        </div>
      </div>
    </div>
  );
}
