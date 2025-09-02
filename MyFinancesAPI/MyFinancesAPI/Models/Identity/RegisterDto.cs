using System.ComponentModel.DataAnnotations;

namespace MyFinancesAPI.Models.Identity
{
    public class RegisterDto : IExternalProvider
    {
        [Required(ErrorMessage = "Confirm Password is required.")]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string? ConfirmPassword { get; set; }

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Email is not valid.")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Frontend Base URL is required.")]
        public string? FrontendBaseUrl { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Password is required.")]
        public string? Password { get; set; }

        public string? ProviderName { get; set; }
        public string? ProviderKey { get; set; }
    }
}