using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api;
using MyFinances.Api.Auth;
using MyFinances.Api.Import;
using MyFinances.Api.Tests.Support;
using MyFinances.Api.Transactions;
using Xunit;

namespace MyFinances.Api.Tests;

// Phase 3 smoke coverage for the parse/commit endpoints: reuses AuthApiFactory's
// WebApplicationFactory + EF Core InMemory swap. Phase 7 adds full fixture-driven
// coverage (real redacted sample, re-import guardrail) via its own factory.
public class ImportEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/antiforgery-token");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        return payload!.Token;
    }

    private record TokenResponse(string Token);

    // Creates an account for the currently-authenticated user via the real endpoint (matching
    // AccountEndpointsTests' style), so import tests exercise the same accountId the user would
    // actually have in hand.
    private static async Task<AccountDto> CreateAccountAsync(HttpClient client, string bankName, string accountNumber, string? bank = null)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/accounts/")
        {
            Content = JsonContent.Create(new AccountWriteRequest(bankName, accountNumber, bank)),
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AccountDto>(JsonOptions);
        return dto!;
    }

    private static async Task<Account> InsertAccountForOtherUserAsync(AuthApiFactory factory, string bank, string number)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = new Account { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), BankName = bank, AccountNumber = number };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    // Reuse the same redacted real-format fixture MBankCsvParserTests parses directly, so the
    // encoding/format is guaranteed correct instead of risking a lossy inline string literal.
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mbank-sample-redacted.csv");

    // The multipart-form-binding IFormFile parameter makes ASP.NET Core's antiforgery
    // middleware (app.UseAntiforgery()) validate this request automatically, even though the
    // endpoint itself has no manual AddEndpointFilter check (that's only needed for JSON-body
    // endpoints like /import/commit) — so every multipart POST here needs a real token.
    private static async Task<HttpRequestMessage> BuildUploadRequestAsync(
        HttpClient client,
        byte[] fileBytes,
        Guid accountId,
        string? bank = null,
        string fileName = "export.csv",
        string contentType = "text/csv")
    {
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(accountId.ToString()), "accountId");
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
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        using var request = await BuildUploadRequestAsync(client, Encoding.UTF8.GetBytes("not,a,recognizable,export\r\n1,2,3,4\r\n"), account.Id);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Parse_AccountIdNotOwnedByUser_ReturnsNotFound()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var otherUsersAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "111");

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), otherUsersAccount.Id);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Parse_AccountIdThatDoesNotExist_ReturnsNotFound()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), Guid.NewGuid());
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Parse_RecognizedMBankFile_ReturnsRowsWithDuplicateFlags()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), account.Id);
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
    public async Task Parse_AccountBankNameMatchesDetectedBank_BankMismatchIsFalse()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), account.Id);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        Assert.False(parsed!.BankMismatch);
    }

    [Fact]
    public async Task Parse_AccountBankDiffersFromDetectedBank_BankMismatchIsTrue()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "Revolut", "111", "Santander Bank Polska");

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), account.Id);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        Assert.True(parsed!.BankMismatch);
    }

    [Fact]
    public async Task Parse_AccountBankIsOther_BankMismatchIsNeverRaised()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "Revolut", "111", "Other");

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), account.Id);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.False(parsed!.BankMismatch);
    }

    [Fact]
    public async Task Parse_AccountLabelIsATypoButBankIsRight_BankMismatchIsFalse()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mbnak glowne", "111", "mBank");

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), account.Id);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.False(parsed!.BankMismatch);
    }

    // Regression test: two stored transactions can legitimately share the same dedup hash
    // (Transaction.Hash isn't unique — see Transaction.cs) once a prior collision was
    // resolved as "Keep" for both. Re-parsing a file that collides with both used to throw
    // "An item with the same key has already been added" from ToDictionaryAsync(t => t.Hash, ...).
    [Fact]
    public async Task Parse_WhenTwoStoredTransactionsShareTheSameHash_DoesNotThrow()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail);
            var userId = user!.Id;

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hash = DedupHash.ComputeHash(userId, new DateOnly(2026, 8, 1), -500.00m, "NA JEDZENIE", account.Id);
            db.Transactions.AddRange(
                new Transaction { Id = Guid.NewGuid(), UserId = userId, AccountId = account.Id, Date = new DateOnly(2026, 8, 1), Description = "NA JEDZENIE", Amount = -500.00m, Hash = hash },
                new Transaction { Id = Guid.NewGuid(), UserId = userId, AccountId = account.Id, Date = new DateOnly(2026, 8, 1), Description = "NA JEDZENIE", Amount = -500.00m, Hash = hash });
            await db.SaveChangesAsync();
        }

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), account.Id);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        Assert.All(
            parsed!.Rows.Where(r => r.Date == new DateOnly(2026, 8, 1) && r.Amount == -500.00m && r.Description == "NA JEDZENIE"),
            row => Assert.True(row.IsDuplicate));
    }

    // Dedup is now scoped per-account (DedupHash.ComputeHash takes accountId, not bank name), so
    // the same transaction data imported under a second account for the same user must not be
    // flagged as a duplicate of the first account's transaction.
    [Fact]
    public async Task Parse_SameTransactionDataUnderDifferentAccount_IsNotFlaggedAsDuplicate()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var firstAccount = await CreateAccountAsync(client, "mBank", "111");
        var secondAccount = await CreateAccountAsync(client, "mBank", "222");

        using var firstRequest = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), firstAccount.Id);
        var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstParsed = await firstResponse.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);

        var firstAntiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var commitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/import/commit");
        commitRequest.Headers.Add("X-XSRF-TOKEN", firstAntiforgeryToken);
        commitRequest.Content = JsonContent.Create(new
        {
            AccountId = firstAccount.Id,
            SkippedErrorCount = firstParsed!.SkippedErrorCount,
            Rows = firstParsed.Rows.Select(r => new { r.Date, r.Description, r.Amount, Decision = "Keep" }),
        });
        var commitResponse = await client.SendAsync(commitRequest);
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);

        using var secondRequest = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), secondAccount.Id);
        var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondParsed = await secondResponse.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(secondParsed);
        Assert.All(secondParsed!.Rows, row => Assert.False(row.IsDuplicate));
    }

    [Fact]
    public async Task Commit_PersistsKeptRowAndCountsSkippedDuplicate()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail);
            userId = user!.Id;

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existingHash = DedupHash.ComputeHash(userId, new DateOnly(2024, 1, 10), 50.00m, "Existing transaction", account.Id);
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountId = account.Id,
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
            AccountId = account.Id,
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
        Assert.Equal(account.Id, summary!.AccountId);
        Assert.Equal(1, summary.ImportedCount);
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
            Assert.Equal(StatementFormat.Csv, batch.SourceFormat);
        }
    }

    [Fact]
    public async Task Commit_AccountIdNotOwnedByUser_ReturnsNotFound()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var otherUsersAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "111");

        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var commitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/import/commit");
        commitRequest.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        commitRequest.Content = JsonContent.Create(new
        {
            AccountId = otherUsersAccount.Id,
            SkippedErrorCount = 0,
            Rows = Array.Empty<object>(),
        });

        var response = await client.SendAsync(commitRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string ErsteFixturePath(string fileName = "erste-sample-redacted.csv") =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static async Task<ImportParseResponse> ParseErsteFixtureAsync(HttpClient client, string bankName, string fileName = "erste-sample-redacted.csv")
    {
        var account = await CreateAccountAsync(client, bankName, "111");
        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(ErsteFixturePath(fileName)), account.Id);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public async Task Parse_RecognizedErsteFile_ReturnsErsteRowsWithoutBankMismatch()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);

        var parsed = await ParseErsteFixtureAsync(client, "Erste");

        Assert.Equal("Erste", parsed.Bank);
        Assert.Equal(29, parsed.Rows.Count);
        Assert.False(parsed.BankMismatch);
    }

    [Fact]
    public async Task Parse_ErsteFileWithMBankAccount_BankMismatchIsTrue()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);

        var parsed = await ParseErsteFixtureAsync(client, "mBank");

        Assert.Equal("Erste", parsed.Bank);
        Assert.True(parsed.BankMismatch);
    }

    [Theory]
    [InlineData("erste-sample-redacted-tab.csv")]
    [InlineData("erste-sample-redacted-pipe.csv")]
    [InlineData("erste-sample-redacted-comma.csv")]
    public async Task Parse_ErsteDelimiterVariants_AreRecognizedThroughTheEndpoint(string fileName)
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);

        var parsed = await ParseErsteFixtureAsync(client, "Erste", fileName);

        Assert.Equal("Erste", parsed.Bank);
        Assert.Equal(21, parsed.Rows.Count);
    }

    // The fixture holds three identical +500,00 "PRZELEW ŚRODKÓW" rows on the same day. After
    // committing all of them, re-parsing the same file must flag every row as a duplicate —
    // including all three identical ones, which share a single dedup hash.
    [Fact]
    public async Task Parse_ErsteFileAfterCommit_FlagsEveryCommittedRowAsDuplicate()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "Erste", "111");
        var fileBytes = await File.ReadAllBytesAsync(ErsteFixturePath());

        using var firstRequest = await BuildUploadRequestAsync(client, fileBytes, account.Id);
        var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstParsed = await firstResponse.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.All(firstParsed!.Rows, row => Assert.False(row.IsDuplicate));

        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var commitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/import/commit");
        commitRequest.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        commitRequest.Content = JsonContent.Create(new
        {
            AccountId = account.Id,
            SkippedErrorCount = firstParsed.SkippedErrorCount,
            Rows = firstParsed.Rows.Select(r => new { r.Date, r.Description, r.Amount, Decision = "Keep" }),
        });
        var commitResponse = await client.SendAsync(commitRequest);
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);

        using var secondRequest = await BuildUploadRequestAsync(client, fileBytes, account.Id);
        var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondParsed = await secondResponse.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(secondParsed);
        Assert.Equal(29, secondParsed!.Rows.Count);
        Assert.All(secondParsed.Rows, row => Assert.True(row.IsDuplicate));
        Assert.Equal(3, secondParsed.Rows.Count(r => r.Amount == 500.00m && r.Description == "PRZELEW ŚRODKÓW"));
    }

    // No parser recognizes this content, but an explicit bank=Erste choice must still
    // resolve the Erste parser (no 400) — which then finds nothing to parse and returns zero rows.
    // (mBank-format content would not do: mBank auto-detection wins over the manual choice.)
    [Fact]
    public async Task Parse_ManualErsteFallbackOnNonErsteFile_ReturnsOkWithZeroRows()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "Erste", "111");

        using var request = await BuildUploadRequestAsync(client, Encoding.UTF8.GetBytes("not,a,recognizable,export\r\n1,2,3,4\r\n"), account.Id, bank: "Erste");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        Assert.Equal("Erste", parsed!.Bank);
        Assert.Empty(parsed.Rows);
    }

    private const string UnrecognizedFormatTitle = "Could not recognize this file's bank format. Select a bank manually and retry.";

    // Only the first bytes matter to the format sniffer; the rest need not be a valid PDF.
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n%not a real statement\n");

    private static async Task<string?> ReadProblemTitleAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("title").GetString();
    }

    // Stands in for the future PDF parsers: claims every file and fails its integrity check.
    private sealed class IntegrityFailingPdfParser : IBankStatementParser
    {
        public const string Title = "Balance check failed on page 2 near 2026-08-01.";

        public string BankName => "StubPdfBank";

        public StatementFormat Format => StatementFormat.Pdf;

        public bool CanParse(Stream fileStream) => true;

        public ParseResult Parse(Stream fileStream) => throw new StatementIntegrityException(Title);
    }

    private static WebApplicationFactory<Program> WithIntegrityFailingPdfParser(AuthApiFactory factory) =>
        factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.AddScoped<IBankStatementParser>(_ => new IntegrityFailingPdfParser())));

    [Fact]
    public async Task Parse_PdfWithBankThatHasNoPdfParser_ReturnsBadRequestNamingBankAndFormat()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        // Erste is the one bank that still reads CSV only (mBank and VeloBank have PDF parsers).
        var account = await CreateAccountAsync(client, "Erste", "111");

        using var request = await BuildUploadRequestAsync(client, PdfBytes, account.Id, bank: "Erste", fileName: "statement.pdf", contentType: "application/pdf");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Erste import does not support PDF files.", await ReadProblemTitleAsync(response));
    }

    [Fact]
    public async Task Parse_PdfWithoutBank_ReturnsTheGenericUnrecognizedBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        using var request = await BuildUploadRequestAsync(client, PdfBytes, account.Id, fileName: "statement.pdf", contentType: "application/pdf");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UnrecognizedFormatTitle, await ReadProblemTitleAsync(response));
    }

    [Fact]
    public async Task Parse_PdfWithUnknownBank_ReturnsTheGenericUnrecognizedBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        using var request = await BuildUploadRequestAsync(client, PdfBytes, account.Id, bank: "NoSuchBank", fileName: "statement.pdf", contentType: "application/pdf");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(UnrecognizedFormatTitle, await ReadProblemTitleAsync(response));
    }

    [Fact]
    public async Task Parse_PdfParserThrowsIntegrityException_Returns422WithItsMessageAndNoRows()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithIntegrityFailingPdfParser(baseFactory);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "StubPdfBank", "111");

        using var request = await BuildUploadRequestAsync(client, PdfBytes, account.Id, fileName: "statement.pdf", contentType: "application/pdf");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(IntegrityFailingPdfParser.Title, document.RootElement.GetProperty("title").GetString());
        Assert.False(document.RootElement.TryGetProperty("rows", out _));
    }

    // Candidates are filtered by the sniffed format: a registered PDF parser that claims every
    // file must not intercept a CSV upload, and CSV flows must resolve exactly as before.
    [Fact]
    public async Task Parse_CsvUploadWithPdfParserRegistered_StillResolvesTheCsvParser()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithIntegrityFailingPdfParser(baseFactory);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), account.Id);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        Assert.Equal("mBank", parsed!.Bank);
        Assert.Equal(4, parsed.Rows.Count);
    }

    [Fact]
    public async Task Parse_CsvWithBankThatHasOnlyAPdfParser_ReturnsBadRequestNamingBankAndFormat()
    {
        using var baseFactory = new AuthApiFactory();
        var factory = WithIntegrityFailingPdfParser(baseFactory);
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "StubPdfBank", "111");

        using var request = await BuildUploadRequestAsync(client, Encoding.UTF8.GetBytes("not,a,recognizable,export\r\n1,2,3,4\r\n"), account.Id, bank: "StubPdfBank");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("StubPdfBank import does not support CSV files.", await ReadProblemTitleAsync(response));
    }

    private static async Task<HttpResponseMessage> UploadPdfAsync(HttpClient client, byte[] pdf, Guid accountId, string? bank = null)
    {
        using var request = await BuildUploadRequestAsync(client, pdf, accountId, bank: bank, fileName: "statement.pdf", contentType: "application/pdf");
        return await client.SendAsync(request);
    }

    private static async Task<ImportParseResponse> ParseVeloBankPdfAsync(HttpClient client, byte[] pdf, Guid accountId)
    {
        var response = await UploadPdfAsync(client, pdf, accountId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public async Task Parse_VeloBankOnePagePdf_ReturnsRowsIncludingPendingOnes()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "VeloBank", "111");

        var parsed = await ParseVeloBankPdfAsync(client, VeloBankSampleData.BuildOnePagePdf(), account.Id);

        Assert.Equal("VeloBank", parsed.Bank);
        Assert.False(parsed.BankMismatch);
        Assert.Equal(VeloBankSampleData.OnePageRowCount, parsed.Rows.Count);
        Assert.All(parsed.Rows, row => Assert.False(row.IsDuplicate));
        // The two pending card rows have no booking date, so they carry their transaction date.
        Assert.Contains(parsed.Rows, row => row.Date == new DateOnly(2026, 9, 30) && row.Amount == -23.40m);
        Assert.Contains(parsed.Rows, row => row.Date == new DateOnly(2026, 9, 30) && row.Amount == -112.05m);
    }

    [Fact]
    public async Task Parse_VeloBankPdfWithMBankAccount_BankMismatchIsTrue()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var parsed = await ParseVeloBankPdfAsync(client, VeloBankSampleData.BuildOnePagePdf(), account.Id);

        Assert.Equal("VeloBank", parsed.Bank);
        Assert.True(parsed.BankMismatch);
    }

    [Fact]
    public async Task Parse_VeloBankPdfAfterCommit_FlagsEveryCommittedRowAsDuplicate()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "VeloBank", "111");
        var pdf = VeloBankSampleData.BuildOnePagePdf();

        var firstParsed = await ParseVeloBankPdfAsync(client, pdf, account.Id);
        Assert.All(firstParsed.Rows, row => Assert.False(row.IsDuplicate));

        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var commitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/import/commit");
        commitRequest.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        commitRequest.Content = JsonContent.Create(new
        {
            AccountId = account.Id,
            SkippedErrorCount = firstParsed.SkippedErrorCount,
            Rows = firstParsed.Rows.Select(r => new { r.Date, r.Description, r.Amount, Decision = "Keep" }),
        });
        var commitResponse = await client.SendAsync(commitRequest);
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);

        var secondParsed = await ParseVeloBankPdfAsync(client, pdf, account.Id);

        Assert.Equal(VeloBankSampleData.OnePageRowCount, secondParsed.Rows.Count);
        Assert.All(secondParsed.Rows, row => Assert.True(row.IsDuplicate));
    }

    [Fact]
    public async Task Parse_VeloBankMultiPagePdf_ReturnsAllRows()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "VeloBank", "111");

        var parsed = await ParseVeloBankPdfAsync(client, VeloBankSampleData.BuildMultiPagePdf(), account.Id);

        Assert.Equal("VeloBank", parsed.Bank);
        Assert.Equal(VeloBankSampleData.MultiPageRowCount, parsed.Rows.Count);
    }

    [Fact]
    public async Task Parse_VeloBankPdfWithTamperedBalance_Returns422WithIntegrityMessageAndNoRows()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "VeloBank", "111");

        // Row 5 is a booked PLN card row in the middle of the balance chain.
        var rows = VeloBankSampleData.OnePageRows.ToList();
        rows[5] = rows[5] with { Balance = rows[5].Balance + 1.00m };
        var pdf = VeloBankPdfBuilder.Build(VeloBankSampleData.Header, rows, VeloBankSampleData.OnePageLayout);

        var response = await UploadPdfAsync(client, pdf, account.Id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var title = document.RootElement.GetProperty("title").GetString();
        Assert.StartsWith("VeloBank statement rejected", title);
        Assert.False(document.RootElement.TryGetProperty("rows", out _));
    }

    [Fact]
    public async Task Parse_VeloBankChosenWithCsvContent_ReturnsBadRequestNamingBankAndFormat()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "VeloBank", "111");

        using var request = await BuildUploadRequestAsync(client, Encoding.UTF8.GetBytes("not,a,recognizable,export\r\n1,2,3,4\r\n"), account.Id, bank: "VeloBank");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VeloBank import does not support CSV files.", await ReadProblemTitleAsync(response));
    }

    [Fact]
    public async Task Parse_VeloBankChosenWithCorruptPdf_ReturnsOkWithZeroRows()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "VeloBank", "111");

        var response = await UploadPdfAsync(client, PdfBytes, account.Id, bank: "VeloBank");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        Assert.Equal("VeloBank", parsed!.Bank);
        Assert.Empty(parsed.Rows);
    }

    [Fact]
    public async Task Parse_VeloBankPdfWithAnotherBankChosen_AutoDetectionWins()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var response = await UploadPdfAsync(client, VeloBankSampleData.BuildOnePagePdf(), account.Id, bank: "mBank");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        Assert.Equal("VeloBank", parsed!.Bank);
    }

    private const string MBankPdfFixture = "mbank-pdf-sample-synthetic.pdf";

    private static byte[] ReadMBankPdfFixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", MBankPdfFixture));

    private static async Task<ImportParseResponse> ParseMBankPdfAsync(HttpClient client, byte[] pdf, Guid accountId)
    {
        var response = await UploadPdfAsync(client, pdf, accountId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public async Task Parse_MBankPdfWithMBankAccount_ReturnsRowsAsPdfWithoutBankMismatch()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var parsed = await ParseMBankPdfAsync(client, ReadMBankPdfFixture(), account.Id);

        Assert.Equal("mBank", parsed.Bank);
        Assert.False(parsed.BankMismatch);
        Assert.Equal(StatementFormat.Pdf, parsed.Format);
        Assert.Equal(MBankSampleData.TwoPageRowCount, parsed.Rows.Count);
        Assert.All(parsed.Rows, row => Assert.False(row.IsDuplicate));
        Assert.Contains(parsed.Rows, row => row.Date == new DateOnly(2026, 9, 17) && row.Amount == 3200.00m);
    }

    [Fact]
    public async Task Parse_MBankMultiPagePdf_ReturnsAllRows()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var parsed = await ParseMBankPdfAsync(client, MBankSampleData.BuildMultiPagePdf(), account.Id);

        Assert.Equal("mBank", parsed.Bank);
        Assert.Equal(MBankSampleData.MultiPageRowCount, parsed.Rows.Count);
    }

    [Fact]
    public async Task Parse_MBankPdfWithVeloBankAccount_BankMismatchIsTrue()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "VeloBank", "111");

        var parsed = await ParseMBankPdfAsync(client, ReadMBankPdfFixture(), account.Id);

        Assert.Equal("mBank", parsed.Bank);
        Assert.True(parsed.BankMismatch);
    }

    [Fact]
    public async Task Parse_MBankPdfAfterCommit_FlagsEveryCommittedRowAsDuplicate()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var pdf = ReadMBankPdfFixture();

        var firstParsed = await ParseMBankPdfAsync(client, pdf, account.Id);
        Assert.All(firstParsed.Rows, row => Assert.False(row.IsDuplicate));

        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var commitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/import/commit");
        commitRequest.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        commitRequest.Content = JsonContent.Create(new
        {
            AccountId = account.Id,
            SkippedErrorCount = firstParsed.SkippedErrorCount,
            SourceFormat = firstParsed.Format.ToString(),
            Rows = firstParsed.Rows.Select(r => new { r.Date, r.Description, r.Amount, Decision = "Keep" }),
        });
        var commitResponse = await client.SendAsync(commitRequest);
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);

        var secondParsed = await ParseMBankPdfAsync(client, pdf, account.Id);

        Assert.Equal(MBankSampleData.TwoPageRowCount, secondParsed.Rows.Count);
        Assert.All(secondParsed.Rows, row => Assert.True(row.IsDuplicate));
    }

    [Fact]
    public async Task Parse_MBankPdfWithTamperedBalance_Returns422WithIntegrityMessageAndNoRows()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var rows = MBankSampleData.TwoPageRows.ToList();
        rows[5] = rows[5] with { Balance = rows[5].Balance + 1.00m };
        var pdf = MBankPdfBuilder.Build(MBankSampleData.Header, MBankSampleData.OpeningBalance, rows, MBankSampleData.TwoPageLayout);

        var response = await UploadPdfAsync(client, pdf, account.Id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var title = document.RootElement.GetProperty("title").GetString();
        Assert.StartsWith("mBank statement rejected", title);
        Assert.False(document.RootElement.TryGetProperty("rows", out _));
    }

    [Fact]
    public async Task Parse_MBankChosenWithCorruptPdf_ReturnsOkWithZeroRows()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var response = await UploadPdfAsync(client, PdfBytes, account.Id, bank: "mBank");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        Assert.Equal("mBank", parsed!.Bank);
        Assert.Equal(StatementFormat.Pdf, parsed.Format);
        Assert.Empty(parsed.Rows);
    }

    [Fact]
    public async Task Parse_MBankCsvIntoAccountHoldingAnOverlappingPdfBatch_WarnsAboutTheOverlap()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, StatementFormat.Pdf, new DateOnly(2026, 8, 3));

        var parsed = await ParseMBankCsvAsync(client, account.Id);

        Assert.Equal("mBank", parsed.Bank);
        Assert.Equal(StatementFormat.Csv, parsed.Format);
        Assert.True(parsed.MixedFormatOverlapCount > 0);
    }

    // The mBank CSV fixture parses to four rows dated 2026-08-01 .. 2026-08-05 (the fifth is skipped).
    private static readonly DateOnly FixtureFirstDate = new(2026, 8, 1);
    private static readonly DateOnly FixtureLastDate = new(2026, 8, 5);

    private static async Task<Guid> GetUserIdAsync(AuthApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByEmailAsync(AuthApiFactory.AllowedEmail);
        return user!.Id;
    }

    // Seeds one transaction per date; with a source format they hang off a new batch of that
    // format, with null they are manual entries (no batch).
    private static async Task SeedTransactionsAsync(
        AuthApiFactory factory,
        Guid userId,
        Guid accountId,
        StatementFormat? sourceFormat,
        params DateOnly[] dates)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Guid? batchId = null;
        if (sourceFormat is not null)
        {
            var batch = new ImportBatch
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountId = accountId,
                ImportedAtUtc = DateTime.UtcNow,
                ImportedCount = dates.Length,
                SourceFormat = sourceFormat.Value,
            };
            db.ImportBatches.Add(batch);
            batchId = batch.Id;
        }

        foreach (var date in dates)
        {
            var description = $"Seeded {Guid.NewGuid()}";
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AccountId = accountId,
                Date = date,
                Description = description,
                Amount = -1.00m,
                Hash = DedupHash.ComputeHash(userId, date, -1.00m, description, accountId),
                ImportBatchId = batchId,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<ImportParseResponse> ParseMBankCsvAsync(HttpClient client, Guid accountId)
    {
        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), accountId);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public async Task Parse_CsvUpload_ReportsCsvFormatAndNoOverlapWhenAccountIsEmpty()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var parsed = await ParseMBankCsvAsync(client, account.Id);

        Assert.Equal(StatementFormat.Csv, parsed.Format);
        Assert.Equal(0, parsed.MixedFormatOverlapCount);
    }

    [Fact]
    public async Task Parse_VeloBankPdfUpload_ReportsPdfFormat()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "VeloBank", "111");

        var parsed = await ParseVeloBankPdfAsync(client, VeloBankSampleData.BuildOnePagePdf(), account.Id);

        Assert.Equal(StatementFormat.Pdf, parsed.Format);
    }

    [Fact]
    public async Task Parse_FormatIsSerializedAsAString()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        using var request = await BuildUploadRequestAsync(client, await File.ReadAllBytesAsync(FixturePath), account.Id);
        var response = await client.SendAsync(request);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Csv", document.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Parse_OtherFormatRowsInsideRange_AreCounted()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, StatementFormat.Pdf, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4));

        var parsed = await ParseMBankCsvAsync(client, account.Id);

        Assert.Equal(3, parsed.MixedFormatOverlapCount);
    }

    [Fact]
    public async Task Parse_OtherFormatRowsExactlyAtRangeBoundaries_AreCounted()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, StatementFormat.Pdf, FixtureFirstDate, FixtureLastDate);

        var parsed = await ParseMBankCsvAsync(client, account.Id);

        Assert.Equal(2, parsed.MixedFormatOverlapCount);
    }

    [Fact]
    public async Task Parse_OtherFormatRowsOutsideRange_AreNotCounted()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, StatementFormat.Pdf, FixtureFirstDate.AddDays(-1), FixtureLastDate.AddDays(1));

        var parsed = await ParseMBankCsvAsync(client, account.Id);

        Assert.Equal(0, parsed.MixedFormatOverlapCount);
    }

    [Fact]
    public async Task Parse_SameFormatRowsInsideRange_AreNotCounted()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, StatementFormat.Csv, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 3));

        var parsed = await ParseMBankCsvAsync(client, account.Id);

        Assert.Equal(0, parsed.MixedFormatOverlapCount);
    }

    [Fact]
    public async Task Parse_ManualEntriesInsideRange_AreNotCounted()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, null, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 3));

        var parsed = await ParseMBankCsvAsync(client, account.Id);

        Assert.Equal(0, parsed.MixedFormatOverlapCount);
    }

    [Fact]
    public async Task Parse_OtherFormatRowsOnAnotherAccountOfTheSameUser_AreNotCounted()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var otherAccount = await CreateAccountAsync(client, "mBank", "222");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, otherAccount.Id, StatementFormat.Pdf, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 3));

        var parsed = await ParseMBankCsvAsync(client, account.Id);

        Assert.Equal(0, parsed.MixedFormatOverlapCount);
    }

    [Fact]
    public async Task Parse_OtherFormatRowsOfAnotherUser_AreNotCounted()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var otherUsersAccount = await InsertAccountForOtherUserAsync(factory, "mBank", "333");
        await SeedTransactionsAsync(factory, otherUsersAccount.UserId, otherUsersAccount.Id, StatementFormat.Pdf, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 3));

        var parsed = await ParseMBankCsvAsync(client, account.Id);

        Assert.Equal(0, parsed.MixedFormatOverlapCount);
    }

    [Fact]
    public async Task Parse_ZeroParsedRows_ReportsZeroOverlap()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");
        var userId = await GetUserIdAsync(factory);
        await SeedTransactionsAsync(factory, userId, account.Id, StatementFormat.Pdf, new DateOnly(2026, 8, 2));

        // Manual-bank fallback on unrecognizable content: the mBank CSV parser finds no rows.
        using var request = await BuildUploadRequestAsync(client, Encoding.UTF8.GetBytes("not,a,recognizable,export\r\n1,2,3,4\r\n"), account.Id, bank: "mBank");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var parsed = await response.Content.ReadFromJsonAsync<ImportParseResponse>(JsonOptions);
        Assert.NotNull(parsed);
        Assert.Empty(parsed!.Rows);
        Assert.Equal(0, parsed.MixedFormatOverlapCount);
    }

    private static async Task<ImportSummaryDto> CommitSingleRowAsync(HttpClient client, Guid accountId, object? sourceFormat, bool includeSourceFormat)
    {
        var antiforgeryToken = await GetAntiforgeryTokenAsync(client);
        using var commitRequest = new HttpRequestMessage(HttpMethod.Post, "/api/import/commit");
        commitRequest.Headers.Add("X-XSRF-TOKEN", antiforgeryToken);
        var rows = new[] { new { Date = "2026-08-01", Description = "Some row", Amount = 10.00m, Decision = "Keep" } };
        commitRequest.Content = includeSourceFormat
            ? JsonContent.Create(new { AccountId = accountId, SkippedErrorCount = 0, Rows = rows, SourceFormat = sourceFormat })
            : JsonContent.Create(new { AccountId = accountId, SkippedErrorCount = 0, Rows = rows });

        var response = await client.SendAsync(commitRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<ImportSummaryDto>(JsonOptions);
        return summary!;
    }

    [Theory]
    [InlineData("Pdf", StatementFormat.Pdf)]
    [InlineData("Csv", StatementFormat.Csv)]
    public async Task Commit_StoresTheSentSourceFormatOnTheBatch(string sent, StatementFormat expected)
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var summary = await CommitSingleRowAsync(client, account.Id, sent, includeSourceFormat: true);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.SingleAsync(b => b.Id == summary.ImportBatchId);
        Assert.Equal(expected, batch.SourceFormat);
    }

    [Fact]
    public async Task Commit_WithoutSourceFormat_DefaultsToCsv()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var summary = await CommitSingleRowAsync(client, account.Id, null, includeSourceFormat: false);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var batch = await db.ImportBatches.SingleAsync(b => b.Id == summary.ImportBatchId);
        Assert.Equal(StatementFormat.Csv, batch.SourceFormat);
    }

    [Fact]
    public async Task Commit_Then_ParseOtherFormat_WarnsAboutTheCommittedBatch()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        // A Pdf batch with one row on 2026-08-01, then the mBank CSV (range 08-01..08-05).
        await CommitSingleRowAsync(client, account.Id, "Pdf", includeSourceFormat: true);
        var parsed = await ParseMBankCsvAsync(client, account.Id);

        Assert.Equal(1, parsed.MixedFormatOverlapCount);
    }

    [Fact]
    public async Task Commit_WithoutAntiforgeryToken_ReturnsBadRequest()
    {
        using var factory = new AuthApiFactory();
        using var client = await TestClientHelpers.CreateAuthenticatedClientAsync(factory);
        var account = await CreateAccountAsync(client, "mBank", "111");

        var response = await client.PostAsJsonAsync("/api/import/commit", new
        {
            AccountId = account.Id,
            SkippedErrorCount = 0,
            Rows = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
