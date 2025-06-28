using System.ComponentModel.DataAnnotations;

namespace MyFinancesAPI.Models.Identity
{
    public class ForgotPasswordDto
    {
        [Required(ErrorMessage = "Email is required.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Base Url to the forgot password page is required.")]
        public string FrontedBaseUrl { get; set; } = string.Empty;
    }
}
