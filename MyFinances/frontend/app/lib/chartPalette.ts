// Must stay in sync with the --chart-1..12 tokens in app/app.css.
export const CHART_SLOT_COUNT = 12;

// A category's stored colour slot is unbounded (users can add any number of categories), so slots
// past the token ramp rotate the hue by the golden angle to keep neighbouring slots apart.
export function chartColor(slot: number): string {
  if (slot < CHART_SLOT_COUNT) {
    return `var(--chart-${Math.max(slot, 0) + 1})`;
  }
  const hue = (30 + (slot - CHART_SLOT_COUNT) * 137.508) % 360;
  return `oklch(0.72 0.14 ${hue.toFixed(1)})`;
}
