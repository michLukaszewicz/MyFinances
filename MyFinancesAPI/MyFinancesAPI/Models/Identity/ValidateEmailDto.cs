using System.ComponentModel.DataAnnotations;

namespace MyFinancesAPI.Models.Identity
{
    public class ValidateEmailDto
    {
        [Required(ErrorMessage = "UserId is required.")]
        public string? UserId { get; set; }

        [Required(ErrorMessage = "Token is required.")]
        public string? Token { get; set; }
    }
}