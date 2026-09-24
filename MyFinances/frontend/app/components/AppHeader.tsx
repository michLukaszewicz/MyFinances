import { useNavigate } from "react-router";
import { apiFetch } from "../lib/api";
import logo from "../assets/myfinances-logo.png";

interface AppHeaderProps {
  authenticated: boolean;
}

export function AppHeader({ authenticated }: AppHeaderProps) {
  const navigate = useNavigate();

  async function handleLogout() {
    await apiFetch("/auth/logout", { method: "POST" });
    navigate("/login");
  }

  return (
    <header className="sticky top-0 z-50 flex items-center justify-between border-b border-white/5 bg-gray-950/70 px-6 py-4 backdrop-blur-md">
      <img src={logo} alt="MyFinances" className="h-7 w-auto" />
      {authenticated ? (
        <button
          type="button"
          onClick={handleLogout}
          className="rounded-md px-3 py-1.5 text-sm font-medium text-gray-300 transition-colors hover:bg-white/5 hover:text-white"
        >
          Log out
        </button>
      ) : (
        <nav className="flex items-center gap-2 text-sm">
          <a
            href="/login"
            className="rounded-md px-3 py-1.5 font-medium text-gray-300 transition-colors hover:bg-white/5 hover:text-white"
          >
            Log in
          </a>
          <a
            href="/register"
            className="rounded-md bg-brand-500 px-3 py-1.5 font-medium text-white shadow-sm shadow-brand-900/40 transition-all duration-200 hover:bg-brand-600 hover:shadow-md hover:shadow-brand-600/30 active:scale-95"
          >
            Register
          </a>
        </nav>
      )}
    </header>
  );
}
