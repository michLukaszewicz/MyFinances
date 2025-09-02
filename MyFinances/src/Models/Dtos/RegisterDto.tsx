export type RegisterDto = {
    Name: string;
    Email: string;
    Password: string;
    ConfirmPassword: string;
    FrontendBaseUrl: string;
    ProviderName?: string;
    ProviderKey?: string;
}