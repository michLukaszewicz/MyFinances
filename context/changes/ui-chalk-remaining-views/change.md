---
change_id: ui-chalk-remaining-views
title: Apply the Chalk standard to the remaining views and favicon
status: implementing
created: 2026-10-07
updated: 2026-10-07
archived_at: null
---

## Notes

Request (user, 2026-10-07): make the favicon orange (matching the recoloured logo, `--primary` #E78A53), then migrate the remaining views — import, categorize, settings, login, register — to the Chalk standard established by `ui-chalk-theme-dashboard` (semantic tokens, Button/Input/Card from `app/components/ui`, `chartColor` for charts, dark-only, visible focus ring). See the UI conventions in CLAUDE.md. Goal: remove `--color-brand-*` and literal gray/red/emerald/amber/hex colours from these routes; then update the CLAUDE.md UI rule (brand-* no longer used).

Separate change on purpose (new scope, not folded into the dashboard change).
