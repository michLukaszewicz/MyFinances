# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Use feature branches, merge via PR — never commit/push directly to `main`

- **Context**: Every task we make (any work involving code changes in this repo).
- **Problem**: Committing and pushing directly to `main`/master risks losing progress and breaking the production branch with no safety net — there's no review step or easy rollback point before changes land on the branch that ships.
- **Rule**: Every task should start its own feature branch off `main`; changes are merged back via a pull request, never committed or pushed directly to `main`.
- **Applies to**: implement

## Sync GitHub issues with roadmap.md after every commit or merged PR

- **Context**: Any commit or merged PR that changes `context/foundation/roadmap.md`'s status column, or completes a phase/change tracked there.
- **Problem**: GitHub issues (F-NN/S-NN) are opened once from the roadmap and never re-synced — they drifted silently out of date (all 9 issues stayed `OPEN` with no status while roadmap.md moved several items to `in-progress`/`planning`), so the issue tracker stopped reflecting reality.
- **Rule**: After every commit or merged PR that changes a roadmap item's status, check `context/foundation/roadmap.md`'s current state and update the matching GitHub issue (status label, and a comment or close if done) to match.
- **Applies to**: implement
