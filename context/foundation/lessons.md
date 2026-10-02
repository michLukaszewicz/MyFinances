# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Write every test with Arrange/Act/Assert sections and a shared per-class setup

- **Context**: Any new or changed test method under `MyFinances/backend/Tests` (xUnit), and any test class being touched.
- **Problem**: Tests without visible sections and with setup repeated in each method hid what a test actually varied; reviewers could not tell the standard configuration from the case under test (seen while reviewing the import dedup tests, where every test repeated factory, client and account creation).
- **Rule**: Mark every test with `// Arrange`, `// Act` and `// Assert` comments (`// Act & Assert` only when the call sits inside an assertion), and move setup shared by a class into its constructor or `IAsyncLifetime`, so a test's Arrange holds only what differs from the class's standard configuration.
- **Applies to**: implement, impl-review
