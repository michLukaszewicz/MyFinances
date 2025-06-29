using MyFinancesAPI.Exceptions;
using MyFinancesAPI.Services.EmailServices.Abstractions;
using System.Web;

namespace MyFinancesAPI.Services.EmailServices
{
    public class EmailService(ILogger<EmailService> logger, IEmailClient emailClient) : IEmailService
    {
        private const string subject = "Reset Your Password - MyFinances";
        private readonly ILogger<EmailService> logger = logger;
        private readonly IEmailClient emailClient = emailClient;

        public async Task SendForgotPasswordAsync(string toEmail, string token, string userId, string frontendUrl)
        {
            //TODO: Sprawdzić poprawność tego, może uda się skrócić. Też zastanawiam się czy nie użyć inndego encodowania
            string cleanFrontendUrl = frontendUrl.TrimEnd('/');
            string encodedToken = HttpUtility.UrlEncode(token);
            string encodedUserId = HttpUtility.UrlEncode(userId);
            string encodedUrl = $"{cleanFrontendUrl}?userId={encodedUserId}&token={encodedToken}";
            string safeHref = System.Net.WebUtility.HtmlEncode(encodedUrl);

            string body = $@"<p>To reset your password, please click the link below:</p>
                             <a href='{safeHref}'>Reset Password</a>
                             <p>Or copy and paste this link into your browser:</p>
                             <p>{safeHref}</p>";
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