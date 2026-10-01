using MyFinances.Api.Import;

namespace MyFinances.Api.DI;

// Registered as IBankStatementParser so the import endpoints resolve every known bank
// parser generically via IEnumerable<IBankStatementParser> — adding Revolut later
// (S-07) means only adding another AddScoped line here, no endpoint changes.
public static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddImportServices(this IServiceCollection services)
    {
        services.AddScoped<IBankStatementParser, MBankCsvParser>();
        services.AddScoped<IBankStatementParser, ErsteCsvParser>();
        return services;
    }
}
