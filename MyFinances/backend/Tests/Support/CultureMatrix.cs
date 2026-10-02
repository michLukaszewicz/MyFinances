using System.Globalization;

namespace MyFinances.Api.Tests.Support;

// Runs a synchronous action under each culture of a fixed set, so a test can show that the result
// does not depend on the thread's culture. CurrentCulture is per thread, so this is safe under
// xUnit's parallelism as long as nothing awaits inside the action; both cultures are restored in a
// finally so the setting never leaks into another test.
public static class CultureMatrix
{
    // Culture names shared by every culture test; "" is the invariant culture.
    public static readonly IReadOnlyList<string> Names = ["en-US", "de-DE", "tr-TR", "pl-PL", ""];

    // Runs the function once per culture and returns each result with the culture it ran under.
    // The caller asserts every result against an authored literal, never against the first run.
    public static IReadOnlyList<(string Culture, T Result)> Run<T>(Func<T> action)
    {
        var results = new List<(string, T)>();
        foreach (var name in Names)
        {
            var culture = CultureInfo.GetCultureInfo(name);
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                results.Add((name.Length == 0 ? "Invariant" : name, action()));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        return results;
    }
}
