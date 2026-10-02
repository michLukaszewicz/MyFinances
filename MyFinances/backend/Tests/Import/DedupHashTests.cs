using MyFinances.Api.Import;
using Xunit;

namespace MyFinances.Api.Tests;

public class DedupHashTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Date = new(2026, 8, 1);
    private const decimal Amount = -500.00m;
    private const string Description = "NA JEDZENIE";
    private static readonly Guid AccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void ComputeHash_ReturnsIdenticalHash_ForIdenticalInputs()
    {
        // Arrange
        var first = DedupHash.ComputeHash(UserId, Date, Amount, Description, AccountId);

        // Act
        var second = DedupHash.ComputeHash(UserId, Date, Amount, Description, AccountId);

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeHash_Differs_WhenUserIdDiffers()
    {
        // Arrange
        var baseline = DedupHash.ComputeHash(UserId, Date, Amount, Description, AccountId);

        // Act
        var other = DedupHash.ComputeHash(Guid.Parse("22222222-2222-2222-2222-222222222222"), Date, Amount, Description, AccountId);

        // Assert
        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void ComputeHash_Differs_WhenDateDiffers()
    {
        // Arrange
        var baseline = DedupHash.ComputeHash(UserId, Date, Amount, Description, AccountId);

        // Act
        var other = DedupHash.ComputeHash(UserId, Date.AddDays(1), Amount, Description, AccountId);

        // Assert
        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void ComputeHash_Differs_WhenAmountDiffers()
    {
        // Arrange
        var baseline = DedupHash.ComputeHash(UserId, Date, Amount, Description, AccountId);

        // Act
        var other = DedupHash.ComputeHash(UserId, Date, Amount - 1.00m, Description, AccountId);

        // Assert
        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void ComputeHash_Differs_WhenDescriptionDiffers()
    {
        // Arrange
        var baseline = DedupHash.ComputeHash(UserId, Date, Amount, Description, AccountId);

        // Act
        var other = DedupHash.ComputeHash(UserId, Date, Amount, "OTHER", AccountId);

        // Assert
        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void ComputeHash_Differs_WhenAccountIdDiffers()
    {
        // Arrange
        var baseline = DedupHash.ComputeHash(UserId, Date, Amount, Description, AccountId);

        // Act
        var other = DedupHash.ComputeHash(UserId, Date, Amount, Description, Guid.Parse("44444444-4444-4444-4444-444444444444"));

        // Assert
        Assert.NotEqual(baseline, other);
    }
}
