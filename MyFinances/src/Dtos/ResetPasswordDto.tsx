export type ResetPasswordDto = {
    Email: string;
    Token: string;
    NewPassword: string;
    ConfirmPassword: string;
}