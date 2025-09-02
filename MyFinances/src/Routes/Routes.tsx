import { createBrowserRouter } from "react-router-dom";
import { appRoutes } from "./AppRoutes";
import { authorizationRoutes } from "./AuthorizationRoutes";

export const router = createBrowserRouter([
  ...appRoutes,
  ...authorizationRoutes,
]);
