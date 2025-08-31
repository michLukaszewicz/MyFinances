export type RegisterDto = {
    Name: string;
    Email: string;
    Password: string;
    ConfirmPassword: string;
    FrontendBaseUrl: string;
    Provider?: string;
    ProviderKey?: string;
}