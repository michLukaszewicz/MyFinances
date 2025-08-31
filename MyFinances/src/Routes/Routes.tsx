import { createBrowserRouter } from "react-router-dom";
import App from "../App";
import LoginPage from "../Pages/LoginPage";
import RegisterPage from "../Pages/RegisterPage";
import ForgotPasswordPage from "../Pages/ForgotPasswordPage";
import DashboardPage from "../Pages/DashboardPage";
import RequiredAuth from "../Components/Auth/RequiredAuth";
import HomePage from "../Pages/HomePage";
import ResetPasswordPage from "../Pages/ResetPasswordPage";
import EmailConfirmationPage from "../Pages/EmailConfirmationPage";
import CompleteRegistrationPage from "../Pages/CompleteRegistrationPage";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <App />,
    children: [
      {
        path: "",
        element: (
          <RequiredAuth requireLoggedOut={true} redirectTo="/dashboard">
            <HomePage />
          </RequiredAuth>
        ),
      },
      {
        path: "dashboard",
        element: (
          <RequiredAuth requireLoggedOut={false} redirectTo="/login">
            <DashboardPage />
          </RequiredAuth>
        ),
      },
      {
        path: "login",
        element: (
          <RequiredAuth requireLoggedOut={true}>
            <LoginPage />
          </RequiredAuth>
        ),
      },
      {
        path: "register",
        element: (
          <RequiredAuth requireLoggedOut={true}>
            <RegisterPage />
          </RequiredAuth>
        ),
      },
      {
        path: "complete-registration",
        element: (
          <RequiredAuth requireLoggedOut={true}>
            <CompleteRegistrationPage />
          </RequiredAuth>
        ),
      },
      {
        path: "forgot-password",
        element: (
          <RequiredAuth requireLoggedOut={true}>
            <ForgotPasswordPage />
          </RequiredAuth>
        ),
      },
      {
        path: "reset-password",
        element: (
          <RequiredAuth requireLoggedOut={false}>
            <ResetPasswordPage />
          </RequiredAuth>
        ),
      },
      {
        path: "email-confirmation",
        element: (
          <EmailConfirmationPage />
        )
      }
    ],
  },
]);
