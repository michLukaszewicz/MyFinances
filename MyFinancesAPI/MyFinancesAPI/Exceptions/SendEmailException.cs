namespace MyFinancesAPI.Exceptions
{
    public class SendEmailException : Exception
    {
        public SendEmailException() : base("An error occurred while sending the email.")
        {
        }

        public SendEmailException(string message) : base(message)
        {
        }

        public SendEmailException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}