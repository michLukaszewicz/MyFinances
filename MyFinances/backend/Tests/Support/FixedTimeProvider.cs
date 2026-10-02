namespace MyFinances.Api.Tests;

// Shared test clock: always reports the given UTC instant.
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
