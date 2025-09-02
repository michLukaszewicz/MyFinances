namespace MyFinancesAPI.Models.Identity
{
    public interface IExternalProvider
    {
        string? ProviderKey { get; set; }
        string? ProviderName { get; set; }
    }
}