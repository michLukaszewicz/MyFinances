export const ROUTES = {
    home: "/",
    dashboard: "/dashboard",
    profile: "/profile",
    summary: "/summary",
    finances: "/finances",
    accounts: "/accounts",
    auth: {
        base: "/auth",
        login: "/auth/login",
        register: "/auth/register",
        forgotPassword: "/auth/forgot-password",
        resetPassword: "/auth/reset-password",
        emailConfirmation: "/auth/email-confirmation",
        completeRegistration: "/auth/complete-registration",
        externalLogin: "/auth/external-login"
    }
}

export const API_ROUTES = {
    base: "https://localhost:7121",
    auth: {
        login: "/auth/login",
        register: "/auth/register",
        forgotPassword: "/auth/forgot-password",
        resetPassword: "/auth/reset-password",
        emailConfirmation: "/auth/email-confirmation",
        completeRegistration: "/auth/complete-registration",
        externalLogin: "/auth/external-login",
        validateEmail: "/auth/validate-email"
    }
};