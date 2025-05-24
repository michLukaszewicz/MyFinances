import { createBrowserRouter } from "react-router-dom";
import HomePage from "../Pages/HomePage";
import App from "../App";
import LoginPage from "../Pages/LoginPage";
import RegisterPage from "../Pages/RegisterPage";
import ForgotPasswordPage from "../Pages/ForgotPasswordPage";
import DashboardPage from "../Pages/DashboardPage";
import RequiredAuth from "../Components/Auth/RequiredAuth";
import { isAuthenticated } from "../Services/Api/AuthService";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <App />,
    children: [
      {
        path: "",
        element: isAuthenticated() ? (
          <RequiredAuth>
            <DashboardPage />
          </RequiredAuth>
        ) : (
          <HomePage />
        ),
      },
      { path: "login", element: <LoginPage /> },
      { path: "register", element: <RegisterPage /> },
      { path: "forgot-password", element: <ForgotPasswordPage /> },
    ],
  },
]);
