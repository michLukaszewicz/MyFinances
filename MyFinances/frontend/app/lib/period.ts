// Period selection for the dashboard charts. Dates are local calendar dates in yyyy-MM-dd, the
// inclusive from/to shape the backend validates (PeriodRange.cs).

export type PeriodPreset = "month" | "last30" | "last90" | "custom";

export interface PeriodSelection {
  preset: PeriodPreset;
  // yyyy-MM, used by the "month" preset.
  month: string;
  // yyyy-MM-dd, used by the "custom" preset.
  customFrom: string;
  customTo: string;
}

export interface Period {
  from: string;
  to: string;
  label: string;
  // True only for the current calendar month, where the deviation compares against the same day of earlier months.
  isCurrentMonth: boolean;
}

export const PRESET_LABELS: Record<PeriodPreset, string> = {
  month: "Selected month",
  last30: "Last 30 days",
  last90: "Last 90 days",
  custom: "Custom range",
};

function pad(value: number): string {
  return String(value).padStart(2, "0");
}

export function toDateInputValue(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function toMonthInputValue(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}`;
}

export function defaultSelection(now: Date = new Date()): PeriodSelection {
  const today = toDateInputValue(now);
  return { preset: "month", month: toMonthInputValue(now), customFrom: today, customTo: today };
}

function addDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);
}

// Returns null while the selection is incomplete or invalid (e.g. a custom start after its end,
// a future month), so the caller can keep showing the last valid period.
export function resolvePeriod(selection: PeriodSelection, now: Date = new Date()): Period | null {
  const today = toDateInputValue(now);

  switch (selection.preset) {
    case "last30":
      return { from: toDateInputValue(addDays(now, -29)), to: today, label: "the last 30 days", isCurrentMonth: false };
    case "last90":
      return { from: toDateInputValue(addDays(now, -89)), to: today, label: "the last 90 days", isCurrentMonth: false };
    case "custom": {
      const { customFrom, customTo } = selection;
      if (!customFrom || !customTo || customFrom > customTo || customTo > today) return null;
      return { from: customFrom, to: customTo, label: `${customFrom} – ${customTo}`, isCurrentMonth: false };
    }
    case "month": {
      const match = /^(\d{4})-(\d{2})$/.exec(selection.month);
      if (!match) return null;
      const year = Number(match[1]);
      const monthIndex = Number(match[2]) - 1;
      const first = new Date(year, monthIndex, 1);
      if (toMonthInputValue(first) > toMonthInputValue(now)) return null;
      const last = new Date(year, monthIndex + 1, 0);
      const isCurrentMonth = toMonthInputValue(first) === toMonthInputValue(now);
      return {
        from: toDateInputValue(first),
        to: toDateInputValue(last),
        label: isCurrentMonth
          ? "this month"
          : first.toLocaleDateString(undefined, { month: "long", year: "numeric" }),
        isCurrentMonth,
      };
    }
  }
}

// Query-string fragment for the chart and list endpoints.
export function periodQuery(period: Pick<Period, "from" | "to">): string {
  return `from=${period.from}&to=${period.to}`;
}
