# Chalk theme values (tweakcn, registry name `qrafthive`)

Source: https://tweakcn.com/r/themes/cmjgilzlg000404ju2wgs7uj9 — fetched 2026-10-02. Author per user: Anton Grouchtchak.

## Theme
- font-sans: Outfit, ui-sans-serif, sans-serif, system-ui
- font-mono: JetBrains Mono, ui-monospace, monospace
- font-serif: Merriweather, ui-serif, serif
- radius: 0.75rem; spacing: 0.25rem; tracking-normal: 0rem

## Light (not wired — dark only)
background oklch(1 0 0); foreground oklch(0.2101 0.0318 264.6645); card oklch(1 0 0); popover oklch(1 0 0);
primary oklch(0.6716 0.1368 48.5130); primary-foreground oklch(1 0 0);
secondary oklch(0.5360 0.0398 196.0280); secondary-foreground oklch(1 0 0);
muted oklch(0.9670 0.0029 264.5419); muted-foreground oklch(0.5510 0.0234 264.3637);
accent oklch(0.9491 0 0); accent-foreground oklch(0.2101 0.0318 264.6645);
destructive oklch(0.6368 0.2078 25.3313); destructive-foreground oklch(0.9851 0 0);
border/input oklch(0.9276 0.0058 264.5313); ring oklch(0.6716 0.1368 48.5130);
chart-1 oklch(0.5940 0.0443 196.0233); chart-2 oklch(0.7214 0.1337 49.9802); chart-3 oklch(0.8721 0.0864 68.5474); chart-4 oklch(0.6268 0 0); chart-5 oklch(0.6830 0 0);
sidebar oklch(0.9670 0.0029 264.5419); sidebar-primary = primary; sidebar-accent oklch(1 0 0)

## Dark (wired)
background oklch(0.1797 0.0043 308.1928); foreground oklch(0.8109 0 0);
card oklch(0.1822 0 0); card-foreground oklch(0.8109 0 0);
popover oklch(0.1797 0.0043 308.1928); popover-foreground oklch(0.8109 0 0);
primary oklch(0.7214 0.1337 49.9802); primary-foreground oklch(0.1797 0.0043 308.1928);
secondary oklch(0.5940 0.0443 196.0233); secondary-foreground oklch(0.1797 0.0043 308.1928);
muted oklch(0.2520 0 0); muted-foreground oklch(0.6268 0 0);
accent oklch(0.3211 0 0); accent-foreground oklch(0.8109 0 0);
destructive oklch(0.5940 0.0443 196.0233) — **OVERRIDE in this repo** (collides with secondary); destructive-foreground oklch(0.1797 0.0043 308.1928);
border/input oklch(0.2520 0 0); ring oklch(0.7214 0.1337 49.9802);
charts: same 5 as light;
sidebar oklch(0.1822 0 0); sidebar-foreground oklch(0.8109 0 0); sidebar-primary oklch(0.7214 0.1337 49.9802); sidebar-accent oklch(0.3211 0 0); sidebar-border oklch(0.2520 0 0)

## Shadows (both modes, very soft)
shadow-xs/2xs: 0 1px 4px 0 hsl(0 0% 0% / 0.03)
shadow-sm/shadow: 0 1px 4px 0 hsl(0 0% 0% / 0.05), 0 1px 2px -1px hsl(0 0% 0% / 0.05)
shadow-md: 0 1px 4px 0 hsl(0 0% 0% / 0.05), 0 2px 4px -1px hsl(0 0% 0% / 0.05)
shadow-lg: 0 1px 4px 0 hsl(0 0% 0% / 0.05), 0 4px 6px -1px hsl(0 0% 0% / 0.05)
