using MyFinancesAPI.Exceptions;
using MyFinancesAPI.Services.EmailServices.Abstractions;

namespace MyFinancesAPI.Services.EmailServices
{
    public class EmailService(ILogger<EmailService> logger, IEmailClient emailClient) : IEmailService
    {
        private const string subject = "Reset Your Password - MyFinances";
        private readonly IEmailClient emailClient = emailClient;
        private readonly ILogger<EmailService> logger = logger;

        public async Task SendForgotPasswordAsync(string toEmail, string token, string userId, string frontendUrl)
        {
            string safeUrl = PrepareUrl(token, userId, frontendUrl);
            string body = $@"<p>To reset your password, please click the link below:</p>
                             <a href='{safeUrl}'>Reset Password</a>
                             <p>Or copy and paste this link into your browser:</p>
                             <p>{safeUrl}</p>";
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

        public async Task SendValidateEmailAsync(string toEmail, string token, string userId, string frontendUrl)
        {
            string safeUrl = PrepareUrl(token, userId, frontendUrl);
            string body = $@"<p>To confirm your email address, please click the link below:</p>
                             <a href='{safeUrl}'>Confirm email address</a>
                             <p>Or copy and paste this link into your browser:</p>
                             <p>{safeUrl}</p>";
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

        private static string PrepareUrl(string token, string userId, string frontendUrl)
        {
            string cleanFrontendUrl = frontendUrl.TrimEnd('/');
            string url = $"{cleanFrontendUrl}?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
            string safeUrl = System.Net.WebUtility.HtmlEncode(url);
            return safeUrl;
        }
    }
}