using Microsoft.AspNetCore.Identity;

namespace MyFinancesAPI.Models.OperationResults
{
    public class OperationResult
    {
        public List<IdentityError> Errors { get; set; } = [];
        public DateTimeOffset? ExpireAt { get; set; }
        public bool Succeeded { get; set; }
        public string? Token { get; set; }

        public static OperationResult Failed(params IdentityError[] errors)
        {
            return new OperationResult
            {
                Succeeded = false,
                Errors = [.. errors]
            };
        }

        public static OperationResult Success(string? token = null, DateTimeOffset? expireAt = null)
        {
            return new OperationResult
            {
                Succeeded = true,
                Token = token,
                ExpireAt = expireAt
            };
        }
    }
}