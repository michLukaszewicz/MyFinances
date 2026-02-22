export type LoginDto = {
    Email: string;
    Password: string;
    ProviderName: string | null;
    ProviderKey: string | null;
}