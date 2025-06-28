namespace MyFinancesAPI.Services.EmailServices.Abstractions
{
    public interface IEmailClient
    {
        Task SendEmailAsync(string toEmail, string subject, string body);
    }
}