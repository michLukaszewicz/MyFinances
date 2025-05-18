namespace MyFinancesAPI.Models
{
    public class Transaction
    {
        public int Id { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Category { get; set; } = string.Empty;
        public string BankAccount { get; set; } = string.Empty;
        public string OtherSideOfTransaction { get; set; } = string.Empty;
    }
}
