import { createBrowserRouter } from "react-router-dom";
import App from "../App";
import LoginPage from "../Pages/LoginPage";
import RegisterPage from "../Pages/RegisterPage";
import ForgotPasswordPage from "../Pages/ForgotPasswordPage";
import DashboardPage from "../Pages/DashboardPage";
import RequiredAuth from "../Components/Auth/RequiredAuth";
import HomePage from "../Pages/HomePage";

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
