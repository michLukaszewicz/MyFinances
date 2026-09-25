using System.Security.Cryptography;
using System.Text;

namespace MyFinances.Api.Import;

// Pure, deterministic dedup hash. Formula (userId + date + amount + description + accountId) is
// shape-notes pre-decided — collisions are resolved by routing through the review UI, not by
// making the hash more unique. Not a DB unique constraint (Transaction.Hash isn't one either).
public static class DedupHash
{
    public static string ComputeHash(Guid userId, DateOnly date, decimal amount, string description, Guid accountId)
    {
        var stable = string.Join(
            '|',
            userId.ToString("D"),
            date.ToString("yyyy-MM-dd"),
            amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            description,
            accountId.ToString("D"));

        var bytes = Encoding.UTF8.GetBytes(stable);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }
}
