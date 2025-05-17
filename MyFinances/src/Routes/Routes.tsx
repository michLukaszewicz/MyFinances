import { createBrowserRouter } from "react-router-dom";
import HomePage from "../Pages/HomePage";
import App from "../App";
import LoginPage from "../Pages/LoginPage";
import RegisterPage from "../Pages/RegisterPage";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <App />,
    children: [
        { path: "", element: <HomePage /> },
        { path: "login", element: <LoginPage /> }, 
        { path: "register", element: <RegisterPage /> }, 
    ],
  },
]);
