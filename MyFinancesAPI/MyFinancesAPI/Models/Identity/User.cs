using Microsoft.AspNetCore.Identity;

namespace MyFinancesAPI.Models.Identity
{
    public class User : IdentityUser
    {
        public string FirstName { get; set; } = string.Empty;
    }
}
