using System.ComponentModel.DataAnnotations;

namespace MyFinancesAPI.Models
{
    public class Transaction
    {
        public int Id { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;
        public string Description { get; set; } = string.Empty;
        [Required(ErrorMessage = "Amount is required.")]
        public decimal Amount { get; set; }
        public string Category { get; set; } = string.Empty;
        [Required(ErrorMessage = "Bank account is required.")]
        public string BankAccount { get; set; } = string.Empty;
        public string OtherSideOfTransaction { get; set; } = string.Empty;
    }
}
