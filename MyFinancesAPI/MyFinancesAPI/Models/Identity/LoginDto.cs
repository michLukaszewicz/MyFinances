using System.ComponentModel.DataAnnotations;

namespace MyFinancesAPI.Models.Identity
{
    public class LoginDto : IExternalProvider
    {
        [Required(ErrorMessage = "Email is required.")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        public string? Password { get; set; }

        public string? ProviderKey { get; set; }
        public string? ProviderName { get; set; }
    }
}