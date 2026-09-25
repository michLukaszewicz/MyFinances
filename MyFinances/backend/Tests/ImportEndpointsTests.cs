using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api;
using MyFinances.Api.Auth;
using MyFinances.Api.Import;
using MyFinances.Api.Transactions;
using Xunit;

namespace MyFinances.Api.Tests;

// Phase 3 smoke coverage for the parse/commit endpoints: reuses AuthApiFactory's
// WebApplicationFactory + EF Core InMemory swap. Phase 7 adds full fixture-driven
// coverage (real redacted sample, re-import guardrail) via its own factory.
public class ImportEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(AuthApiFactory factory)
    {
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(AuthApiFactory.AllowedEmail, "correct-horse-battery"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        return client;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        return payload!.Token;
    }

    private record TokenResponse(string Token);

    // Reuse the same redacted real-format fixture MBankCsvParserTests parses directly, so the
    // encoding/format is guaranteed correct instead of risking a lossy inline string literal.
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mbank-sample-redacted.csv");

    // The multipart-form-binding IFormFile parameter makes ASP.NET Core's antiforgery
    // middleware (app.UseAntiforgery()) validate this request automatically, even though the
    // endpoint itself has no manual AddEndpointFilter check (that's only needed for JSON-body
    // endpoints like /import/commit) — so every multipart POST here needs a real token.
    private static async Task<HttpRequestMessage> BuildUploadRequestAsync(HttpClient client, byte[] fileBytes, string? bank = null)
    {
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "export.csv");
        if (bank is not null)
        {
            content.Add(new StringContent(bank), "bank");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/import/parse") { Content = content };
        request.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        return request;
    }

    [Fact]
    public async Task Parse_UnrecognizedFileWithNoBankFallback_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var request = await BuildUploadRequestAsync(client, Encoding.UTF8.GetBytes("not,a,recognizable,export\r\n1,2,3,4\r\n"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Parse_RecognizedMBankFile_ReturnsRowsWithDuplicateFlags()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        Assert.Equal("mBank", parsed!.Bank);
        // Nothing is stored yet for this user, so none of the fixture's rows — including the
        // two same-day/same-amount/same-description BLIK rows — should flag as duplicates.
        Assert.Equal(4, parsed.Rows.Count);
        Assert.All(parsed.Rows, row => Assert.False(row.IsDuplicate));
        Assert.Contains(parsed.Rows, row => row.Date == new DateOnly(2026, 8, 1) && row.Amount == -500.00m && row.Description == "NA JEDZENIE");
    }

    [Fact]
    public async Task Commit_PersistsKeptRowAndCountsSkippedDuplicate()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail);
            userId = user!.Id;

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existingHash = DedupHash.ComputeHash(userId, new DateOnly(2024, 1, 10), 50.00m, "Existing transaction", "mBank");
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Bank = "mBank",
                Date = new DateOnly(2024, 1, 10),
                Description = "Existing transaction",
                Amount = 50.00m,
                Hash = existingHash,
            });
            await db.SaveChangesAsync();
        }

        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var commitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/import/commit");
        commitRequest.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        commitRequest.Content = JsonContent.Create(new
        {
            Bank = "mBank",
            SkippedErrorCount = 1,
            Rows = new[]
            {
                new { Date = "2024-01-10", Description = "Existing transaction", Amount = 50.00m, Decision = "Skip" },
                new { Date = "2024-01-15", Description = "New transaction", Amount = 75.50m, Decision = "Keep" },
            }
        });

        var commitResponse = await client.SendAsync(commitRequest);
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);

        var summary = await commitResponse.Content.ReadFromJsonAsync<ImportSummaryDto>(JsonOptions);
        Assert.NotNull(summary);
        Assert.Equal(1, summary!.ImportedCount);
        Assert.Equal(1, summary.SkippedDuplicateCount);
        Assert.Equal(1, summary.SkippedErrorCount);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var transactions = await db.Transactions.Where(t => t.UserId == userId).ToListAsync();
            Assert.Equal(2, transactions.Count);
            Assert.Contains(transactions, t => t.Description == "New transaction" && t.ImportBatchId == summary.ImportBatchId);
            Assert.DoesNotContain(transactions, t => t.Description == "New transaction2");

            var batch = await db.ImportBatches.SingleAsync(b => b.Id == summary.ImportBatchId);
            Assert.Equal(1, batch.ImportedCount);
            Assert.Equal(1, batch.SkippedDuplicateCount);
            Assert.Equal(1, batch.SkippedErrorCount);
        }
    }

    [Fact]
    public async Task Commit_WithoutAntiforgeryToken_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/import/commit", new
        {
            Bank = "mBank",
            SkippedErrorCount = 0,
            Rows = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
