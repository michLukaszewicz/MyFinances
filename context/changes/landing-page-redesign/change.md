---
change_id: landing-page-redesign
title: Rich animated landing page for logged-out visitors
status: implementing
created: 2026-10-02
updated: 2026-10-07
archived_at: null
---

## Notes

Request (user, 2026-10-02): the logged-out view in `MyFinances/frontend/app/routes/home.tsx` currently has only three hero tiles and login/register links. Make it a real landing page that encourages visitors to stay — not necessarily a lot of content, but an interesting look with animations. Reference: https://sentry.io/welcome/ (long page; the wanted part is its animations and visual interest).

Sequencing: depends on the Chalk tokens and Button/Card components from `ui-chalk-theme-dashboard` (Phase 1). That change's Phase 3 only re-tokenises the existing landing; this change replaces its content and layout. Plan it after that Phase 1 lands. Separate change on purpose (new design/UX scope, not folded into the theme plan).

Constraints carried over: CSS-only animation / minimal-dependency stance (`context/archive/2026-10-02-homepage-visual-refresh/plan-brief.md:24`), `prefers-reduced-motion` respected (`app.css` reduced-motion block), visible focus on all interactive elements, English UI copy.
