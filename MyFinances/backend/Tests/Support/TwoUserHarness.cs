using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api.Auth;
using MyFinances.Api.Categorization;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Auth;
using MyFinances.Api.Transactions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MyFinances.Api.Tests.Support;

internal sealed class TwoUserHarness : IDisposable
{
    public const string OtherEmail = "other@example.com";
    public const string Password = "correct-horse-battery";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public HttpClient ClientA { get; }
    public HttpClient ClientB { get; }
    public AuthApiFactory Factory { get; }
    public Guid UserIdA { get; }
    public Guid UserIdB { get; }

    private TwoUserHarness(AuthApiFactory factory, HttpClient clientA, HttpClient clientB, Guid userIdA, Guid userIdB)
    {
        Factory = factory;
        ClientA = clientA;
        ClientB = clientB;
        UserIdA = userIdA;
        UserIdB = userIdB;
    }

    public static async Task<TwoUserHarness> CreateAsync()
    {
        var factory = new AuthApiFactory();
        var clientA = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var userIdA = await ImportTestHelpers.GetUserIdAsync(factory);
        Guid userIdB;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var userB = new AppUser { UserName = OtherEmail, Email = OtherEmail };
            var created = await userManager.CreateAsync(userB, Password);
            Assert.True(created.Succeeded);
            userIdB = userB.Id;
        }
        var clientB = factory.CreateClient();
        var login = await clientB.PostAsJsonAsync("/api/auth/login", new LoginRequest(OtherEmail, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return new TwoUserHarness(factory, clientA, clientB, userIdA, userIdB);
    }

    public static async Task<T> GetJsonAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var token = await ImportTestHelpers.GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        request.Headers.Add("X-XSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    public void Dispose()
    {
        ClientA.Dispose();
        ClientB.Dispose();
        Factory.Dispose();
    }

    public async Task<(Guid ExpenseId, Guid SecondExpenseId, Guid IncomeId)> GetCategoryIdsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expenses = await db.Categories
            .Where(c => c.Kind == CategoryKind.Expense)
            .OrderBy(c => c.SortOrder)
            .Select(c => c.Id)
            .Take(2)
            .ToListAsync();
        var incomeId = await db.Categories
            .Where(c => c.Kind == CategoryKind.Income)
            .OrderBy(c => c.SortOrder)
            .Select(c => c.Id)
            .FirstAsync();
        return (expenses[0], expenses[1], incomeId);
    }

    public async Task<Account> SeedAccountAsync(Guid userId, string accountNumber, string bankName = "mBank")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), UserId = userId, BankName = bankName, AccountNumber = accountNumber };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    public async Task<ImportBatch> SeedImportBatchAsync(Guid userId, Guid accountId, int importedCount)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = accountId,
            ImportedAtUtc = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc),
            ImportedCount = importedCount,
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch;
    }

    public async Task<Transaction> SeedTransactionAsync(
        Guid userId,
        Guid accountId,
        DateOnly date,
        decimal amount,
        string description,
        Guid? categoryId = null,
        Guid? importBatchId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = accountId,
            Date = date,
            Description = description,
            Amount = amount,
            Hash = DedupHash.ComputeHash(userId, date, amount, description, accountId),
            CategoryId = categoryId,
            ImportBatchId = importBatchId,
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        return transaction;
    }

    public async Task<List<string>> SnapshotAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accounts = (await db.Accounts.Where(a => a.UserId == userId).ToListAsync())
            .Select(a => $"account|{a.Id}|{a.UserId}|{a.BankName}|{a.Bank}|{a.AccountNumber}");
        var transactions = (await db.Transactions.Where(t => t.UserId == userId).ToListAsync())
            .Select(t => $"transaction|{t.Id}|{t.UserId}|{t.AccountId}|{t.Date:yyyy-MM-dd}|{t.Description}|{t.Amount}|{t.Hash}|"
                + $"{t.ImportBatchId}|{t.CategoryId}|{t.IsInternalTransfer}|{t.TransferFlagManuallySet}");
        var batches = (await db.ImportBatches.Where(b => b.UserId == userId).ToListAsync())
            .Select(b => $"batch|{b.Id}|{b.UserId}|{b.AccountId}|{b.ImportedAtUtc:O}|{b.ImportedCount}|{b.SkippedDuplicateCount}|"
                + $"{b.SkippedErrorCount}|{b.SourceFormat}");
        return accounts.Concat(transactions).Concat(batches).OrderBy(line => line, StringComparer.Ordinal).ToList();
    }
}