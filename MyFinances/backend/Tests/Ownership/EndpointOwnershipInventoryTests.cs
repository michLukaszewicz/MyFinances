using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MyFinances.Api.Tests.Auth;
using Xunit;

namespace MyFinances.Api.Tests.Ownership;

// Structural guard for Risk #3. Ownership is applied by hand in each endpoint, so a new endpoint
// that forgets the owner filter is the failure this guards against: every mapped /api endpoint
// must be listed below as either covered by an ownership test or as global data with no owner.
// A new endpoint fails this test until it is classified, which forces the question "whose data
// does this touch, and where is the cross-user test?".
public class EndpointOwnershipInventoryTests
{
    private static readonly string[] AnonymousByDesign =
    [
        "POST /api/auth/register",
        "POST /api/auth/login",
        "GET /api/auth/antiforgery-token",
    ];

    private static readonly string[] GlobalNoUserData =
    [
        "GET /api/auth/me",
        "POST /api/auth/logout",
        "GET /api/accounts/banks",
        "GET /api/categorization/categories",
    ];

    private static readonly string[] OwnershipTested =
    [
        "GET /api/accounts/",
        "POST /api/accounts/",
        "PUT /api/accounts/{id:guid}",
        "DELETE /api/accounts/{id:guid}",
        "GET /api/transactions/",
        "POST /api/transactions/",
        "PUT /api/transactions/{id:guid}",
        "DELETE /api/transactions/{id:guid}",
        "GET /api/categorization/queue",
        "GET /api/categorization/handled",
        "PUT /api/categorization/transactions/{id:guid}",
        "POST /api/import/parse",
        "POST /api/import/commit",
        "GET /api/dashboard/category-spend",
        "GET /api/dashboard/category-income",
        "GET /api/dashboard/category-trend",
    ];

    [Fact]
    public void EveryApiEndpoint_IsClassifiedAsOwnershipTestedOrGlobal()
    {
        // Arrange
        var classified = AnonymousByDesign.Concat(GlobalNoUserData).Concat(OwnershipTested).ToHashSet();
        // Act
        var actual = ListApiEndpoints().Select(e => e.Name).ToList();
        // Assert
        var unclassified = actual.Where(name => !classified.Contains(name)).ToList();
        var stale = classified.Where(name => !actual.Contains(name)).ToList();
        Assert.True(
            unclassified.Count == 0,
            "Endpoints without an ownership classification (add a cross-user test in Tests/Ownership, then list them here): "
            + string.Join(", ", unclassified));
        Assert.True(stale.Count == 0, "Classified endpoints that no longer exist: " + string.Join(", ", stale));
    }

    [Fact]
    public void EveryApiEndpoint_RequiresAuthorization_ExceptTheAnonymousAllowList()
    {
        // Act
        var endpoints = ListApiEndpoints();
        // Assert
        var unprotected = endpoints
            .Where(e => !e.RequiresAuthorization || e.AllowsAnonymous)
            .Select(e => e.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(AnonymousByDesign.OrderBy(name => name, StringComparer.Ordinal), unprotected);
    }

    [Fact]
    public void NoEndpointIsInTwoClassifications()
    {
        // Arrange
        var all = AnonymousByDesign.Concat(GlobalNoUserData).Concat(OwnershipTested).ToList();
        // Assert
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    private static List<(string Name, bool RequiresAuthorization, bool AllowsAnonymous)> ListApiEndpoints()
    {
        using var factory = new AuthApiFactory();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        return endpoints
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api", StringComparison.Ordinal))
            .SelectMany(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Select(method => (
                Name: $"{method} {e.RoutePattern.RawText}",
                RequiresAuthorization: e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(),
                AllowsAnonymous: e.Metadata.GetMetadata<IAllowAnonymous>() is not null)))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }
}