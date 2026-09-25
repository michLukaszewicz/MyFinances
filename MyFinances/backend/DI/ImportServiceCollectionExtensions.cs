using MyFinances.Api.Import;

namespace MyFinances.Api.DI;

// Registered as IBankStatementParser so the import endpoints resolve every known bank
// parser generically via IEnumerable<IBankStatementParser> — adding Revolut/Erste later
// (S-07/S-08) means only adding another AddScoped line here, no endpoint changes.
public static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddImportServices(this IServiceCollection services)
    {
        services.AddScoped<IBankStatementParser, MBankCsvParser>();
        return services;
    }
}
