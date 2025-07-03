namespace MyFinancesAPI.Services.EmailServices.Abstractions
{
    public interface IEmailService
    {
        Task SendForgotPasswordAsync(string toEmail, string token, string userId, string frontendUrl);
        Task SendValidateEmailAsync(string toEmail, string token, string userId, string frontendUrl);
    }
}