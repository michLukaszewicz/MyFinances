// Must stay in sync with the --chart-1..12 tokens in app/app.css.
export const CHART_SLOT_COUNT = 12;

export function chartColor(slot: number): string {
  return `var(--chart-${(slot % CHART_SLOT_COUNT) + 1})`;
}
