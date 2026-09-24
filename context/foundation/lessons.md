# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Use feature branches, merge via PR — never commit/push directly to `main`

- **Context**: Every task we make (any work involving code changes in this repo).
- **Problem**: Committing and pushing directly to `main`/master risks losing progress and breaking the production branch with no safety net — there's no review step or easy rollback point before changes land on the branch that ships.
- **Rule**: Every task should start its own feature branch off `main`; changes are merged back via a pull request, never committed or pushed directly to `main`.
- **Applies to**: implement
