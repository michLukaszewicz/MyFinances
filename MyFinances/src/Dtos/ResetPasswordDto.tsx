export type ResetPasswordDto = {
    userId: string;
    Token: string;
    NewPassword: string;
    ConfirmPassword: string;
}