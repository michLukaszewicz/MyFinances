using MyFinancesAPI.Exceptions;
using MyFinancesAPI.Services.EmailServices.Abstractions;

namespace MyFinancesAPI.Services.EmailServices
{
    public class EmailService(ILogger<EmailService> logger, IEmailClient emailClient) : IEmailService
    {
        private const string subject = "Reset Your Password - MyFinances";
        private readonly ILogger<EmailService> logger = logger;
        private readonly IEmailClient emailClient = emailClient;

        public async Task SendForgotPasswordAsync(string toEmail, string token, string userId, string frontendUrl)
        {
            string cleanFrontendUrl = frontendUrl.TrimEnd('/');
            string body = $"<p>To reset your password, please click the link below:</p>" +
                          $"<a href='{cleanFrontendUrl}?userId={userId}&token={token}'>Reset Password</a>";
            try
            {
                await emailClient.SendEmailAsync(toEmail, subject, body);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sending reset password email: {Message}", ex);
                throw new SendEmailException("Error when sending Forgot Password email", ex);
            }
        }

        public async Task SendValidateEmailAsync(string toEmail, string token, string frontendUrl)
        {
            string body = $"<p>To confirm your email address, please click the link below:</p>" +
                          $"<a href='{frontendUrl}/{token}'>Confirm Your Email Address</a>";
            try
            {
                await emailClient.SendEmailAsync(toEmail, subject, body);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sending confirmation email: {Message}", ex.Message);
                throw new SendEmailException("Error when sending Validation email", ex);
            }
        }
    }
}