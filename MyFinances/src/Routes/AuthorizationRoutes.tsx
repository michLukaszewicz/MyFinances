import LoginPage from "../Pages/Authorization/LoginPage";
import RegisterPage from "../Pages/Authorization/RegisterPage";
import ForgotPasswordPage from "../Pages/Authorization/ForgotPasswordPage";
import ResetPasswordPage from "../Pages/Authorization/ResetPasswordPage";
import EmailConfirmationPage from "../Pages/Authorization/EmailConfirmationPage";
import CompleteRegistrationPage from "../Pages/Authorization/CompleteRegistrationPage";
import RequiredAuth from "../Components/Auth/RequiredAuth";
import App from "../App";
import { ROUTES } from "./RoutesConsts";

export const authorizationRoutes = [
  {
    path: ROUTES.auth.base,
    element: <App />,
    children: [
      {
        path: ROUTES.auth.login,
        element: (
          <RequiredAuth requireLoggedOut={true}>
            <LoginPage />
          </RequiredAuth>
        ),
      },
      {
        path: ROUTES.auth.register,
        element: (
          <RequiredAuth requireLoggedOut={true}>
            <RegisterPage />
          </RequiredAuth>
        ),
      },
      {
        path: ROUTES.auth.forgotPassword,
        element: (
          <RequiredAuth requireLoggedOut={true}>
            <ForgotPasswordPage />
          </RequiredAuth>
        ),
      },
      {
        path: ROUTES.auth.resetPassword,
        element: (
          <RequiredAuth requireLoggedOut={false}>
            <ResetPasswordPage />
          </RequiredAuth>
        ),
      },
      {
        path: ROUTES.auth.completeRegistration,
        element: (
          <RequiredAuth requireLoggedOut={true}>
            <CompleteRegistrationPage />
          </RequiredAuth>
        ),
      },
      { path: ROUTES.auth.emailConfirmation, element: <EmailConfirmationPage /> },
    ],
  },
];
