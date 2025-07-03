using System.ComponentModel.DataAnnotations;

namespace MyFinancesAPI.Models.Identity
{
    public class SendValidationEmailDto
    {
        [Required(ErrorMessage = "Email is required.")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Frontend Base URL is required.")]
        public string? FrontendBaseUrl { get; set; }
    }
}