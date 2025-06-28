using MyFinancesAPI.Exceptions;
using MyFinancesAPI.Models;
using MyFinancesAPI.Services.EmailServices.Abstractions;
using System.Net.Mail;

namespace MyFinancesAPI.Services.MailClient
{
    public class EmailClient(EmailSettings settings) : IEmailClient
    {
        private readonly EmailSettings settings = settings;

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var smtpClient = new SmtpClient("smtp.gmail.com")
            {
                Port = 587,
                Credentials = new System.Net.NetworkCredential(settings.Username, settings.Password),
                EnableSsl = true,
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(settings.Username),
                Subject = subject,
                Body = body,
                IsBodyHtml = true,
            };

            mailMessage.To.Add(toEmail);
            await smtpClient.SendMailAsync(mailMessage).ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    throw new SendEmailException(
                        "Failed to send email",
                        task.Exception?.GetBaseException() ?? new Exception("Unknown email send error")
                    );
                }
            });
        }
    }
}