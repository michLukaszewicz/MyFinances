import { createBrowserRouter } from "react-router-dom";
import HomePage from "../Pages/HomePage";
import App from "../App";
import LoginPage from "../Pages/LoginPage";
import RegisterPage from "../Pages/RegisterPage";
import ForgotPasswordPage from "../Pages/ForgotPasswordPage";
import DashboardPage from "../Pages/DashboardPage";
import RequiredAuth from "../Components/Auth/RequiredAuth";
import { isAuthenticated } from "../Services/ApiServices/AuthService";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <App />,
    children: [
      {
        path: "",
        element: isAuthenticated() ? (
          <RequiredAuth requireLoggedOut={false}>
            <DashboardPage />
          </RequiredAuth>
        ) : (
          <HomePage />
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
        path: "forgot-password",
        element: (
          <RequiredAuth requireLoggedOut={true}>
            <ForgotPasswordPage />
          </RequiredAuth>
        ),
      },
    ],
  },
]);
