using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api.Auth;
using MyFinances.Api.Import;
using MyFinances.Api.Transactions;
using Xunit;

namespace MyFinances.Api.Tests.Support;

// Shared parse -> decide -> commit helpers for the import integrity tests. Mirrors the request
// shapes the frontend sends (import.tsx), so tests drive the endpoints the way the client does.
internal static class ImportTestHelpers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private record TokenResponse(string Token);

    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        return payload!.Token;
    }

    public static async Task<AccountDto> CreateAccountAsync(HttpClient client, string bankName, string accountNumber, string? bank = null)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/accounts/")
        {
            Content = JsonContent.Create(new AccountWriteRequest(bankName, accountNumber, bank)),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountDto>(JsonOptions))!;
    }

    // Uploads a statement to /api/import/parse and returns the parsed response (asserts 200).
    public static async Task<ImportParseResponse> ParseAsync(
        HttpClient client,
        byte[] fileBytes,
        Guid accountId,
        string fileName = "export.csv",
        string contentType = "text/csv")
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(accountId.ToString()), "accountId");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/import/parse") { Content = content };
        request.Headers.Add("X-XSRF-TOKEN", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions))!;
    }

    public static Task<ImportParseResponse> ParsePdfAsync(HttpClient client, byte[] pdf, Guid accountId) =>
        ParseAsync(client, pdf, accountId, "statement.pdf", "application/pdf");

    // Commits the parsed rows with a per-row decision, echoing the parse response's format as the
    // client does.
    public static async Task<ImportSummaryDto> CommitAsync(
        HttpClient client,
        Guid accountId,
        ImportParseResponse parsed,
        Func<ImportParseRow, RowDecision> decide)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/import/commit");
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Content = JsonContent.Create(new
        {
            AccountId = accountId,
            SkippedErrorCount = parsed.SkippedErrorCount,
            SourceFormat = parsed.Format.ToString(),
            Rows = parsed.Rows.Select(r => new { r.Date, r.Description, r.Amount, Decision = decide(r).ToString() }),
        });
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportSummaryDto>(JsonOptions))!;
    }

    public static Task<ImportSummaryDto> CommitAllAsync(HttpClient client, Guid accountId, ImportParseResponse parsed) =>
        CommitAsync(client, accountId, parsed, _ => RowDecision.Keep);

    // The client's default flow: skip every flagged duplicate, keep everything else.
    public static Task<ImportSummaryDto> CommitSkippingDuplicatesAsync(HttpClient client, Guid accountId, ImportParseResponse parsed) =>
        CommitAsync(client, accountId, parsed, r => r.IsDuplicate ? RowDecision.Skip : RowDecision.Keep);

    public static async Task<Guid> GetUserIdAsync(AuthApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail);
        return user!.Id;
    }

    // Everything stored for the account, read straight from the database.
    public static async Task<List<Transaction>> GetStoredAsync(AuthApiFactory factory, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Transactions.Where(t => t.AccountId == accountId).ToListAsync();
    }
}
