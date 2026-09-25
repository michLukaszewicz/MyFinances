using MyFinances.Api.Import;
using Xunit;

namespace MyFinances.Api.Tests;

public class DedupHashTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Date = new(2026, 8, 1);
    private const decimal Amount = -500.00m;
    private const string Description = "NA JEDZENIE";
    private const string Bank = "mBank";

    [Fact]
    public void ComputeHash_ReturnsIdenticalHash_ForIdenticalInputs()
    {
        var first = DedupHash.ComputeHash(UserId, Date, Amount, Description, Bank);
        var second = DedupHash.ComputeHash(UserId, Date, Amount, Description, Bank);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeHash_Differs_WhenUserIdDiffers()
    {
        var baseline = DedupHash.ComputeHash(UserId, Date, Amount, Description, Bank);
        var other = DedupHash.ComputeHash(Guid.Parse("22222222-2222-2222-2222-222222222222"), Date, Amount, Description, Bank);

        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void ComputeHash_Differs_WhenDateDiffers()
    {
        var baseline = DedupHash.ComputeHash(UserId, Date, Amount, Description, Bank);
        var other = DedupHash.ComputeHash(UserId, Date.AddDays(1), Amount, Description, Bank);

        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void ComputeHash_Differs_WhenAmountDiffers()
    {
        var baseline = DedupHash.ComputeHash(UserId, Date, Amount, Description, Bank);
        var other = DedupHash.ComputeHash(UserId, Date, Amount - 1.00m, Description, Bank);

        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void ComputeHash_Differs_WhenDescriptionDiffers()
    {
        var baseline = DedupHash.ComputeHash(UserId, Date, Amount, Description, Bank);
        var other = DedupHash.ComputeHash(UserId, Date, Amount, "OTHER", Bank);

        Assert.NotEqual(baseline, other);
    }

    [Fact]
    public void ComputeHash_Differs_WhenBankDiffers()
    {
        var baseline = DedupHash.ComputeHash(UserId, Date, Amount, Description, Bank);
        var other = DedupHash.ComputeHash(UserId, Date, Amount, Description, "Revolut");

        Assert.NotEqual(baseline, other);
    }
}
