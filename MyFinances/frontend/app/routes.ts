import { type RouteConfig, index, route } from "@react-router/dev/routes";

export default [
  index("routes/home.tsx"),
  route("login", "routes/login.tsx"),
  route("register", "routes/register.tsx"),
  route("import", "routes/import.tsx"),
  route("settings", "routes/settings.tsx"),
  route("categorize", "routes/categorize.tsx"),
] satisfies RouteConfig;
