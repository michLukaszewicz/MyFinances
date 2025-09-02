import App from "../App";
import RequiredAuth from "../Components/Auth/RequiredAuth";
import DashboardPage from "../Pages/App/DashboardPage";
import HomePage from "../Pages/App/DashboardPage";
import { ROUTES } from "./RoutesConsts";

export const appRoutes = [
  {
    path: ROUTES.home,
    element: <App />,
    children: [
      {
        path: ROUTES.home,
        element: (
          <RequiredAuth requireLoggedOut={true} redirectTo={ROUTES.dashboard}>
            <HomePage />
          </RequiredAuth>
        ),
      },
      {
        path: ROUTES.dashboard,
        element: (
          <RequiredAuth requireLoggedOut={false} redirectTo={ROUTES.auth.login}>
            <DashboardPage />
          </RequiredAuth>
        ),
      },
    ],
  },
];
