import { Link, useNavigate } from "react-router";
import { apiFetch } from "../lib/api";
import logo from "../assets/myfinances-logo.png";

interface AppHeaderProps {
  authenticated: boolean;
}

export function AppHeader({ authenticated }: AppHeaderProps) {
  const navigate = useNavigate();

  async function handleLogout() {
    try {
      await apiFetch("/auth/logout", { method: "POST" });
    } finally {
      navigate("/login");
    }
  }

  return (
    <header className="sticky top-0 z-50 flex items-center justify-between border-b border-white/5 bg-gray-950/70 px-6 py-4 backdrop-blur-md">
      <div className="flex items-center gap-6">
        <Link to="/" aria-label="MyFinances home">
          <img src={logo} alt="MyFinances" className="h-7 w-auto" />
        </Link>
        {authenticated && (
          <nav className="flex items-center gap-2 text-sm">
            <Link
              to="/"
              className="rounded-md px-3 py-1.5 font-medium text-brand-400 transition-colors hover:bg-white/5 hover:text-brand-300 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
            >
              Dashboard
            </Link>
            <Link
              to="/import"
              className="rounded-md px-3 py-1.5 font-medium text-brand-400 transition-colors hover:bg-white/5 hover:text-brand-300 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
            >
              Import transactions
            </Link>
            <Link
              to="/categorize"
              className="rounded-md px-3 py-1.5 font-medium text-brand-400 transition-colors hover:bg-white/5 hover:text-brand-300 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
            >
              Categorize
            </Link>
            <Link
              to="/settings"
              className="rounded-md px-3 py-1.5 font-medium text-brand-400 transition-colors hover:bg-white/5 hover:text-brand-300 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
            >
              Settings
            </Link>
          </nav>
        )}
      </div>
      {authenticated ? (
        <nav className="flex items-center gap-2 text-sm">
          <button
            type="button"
            onClick={handleLogout}
            className="rounded-md px-3 py-1.5 text-sm font-medium text-brand-400 transition-colors hover:bg-white/5 hover:text-brand-300 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
          >
            Log out
          </button>
        </nav>
      ) : (
        <nav className="flex items-center gap-2 text-sm">
          <a
            href="/login"
            className="rounded-md px-3 py-1.5 font-medium text-brand-400 transition-colors hover:bg-white/5 hover:text-brand-300 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
          >
            Log in
          </a>
          <a
            href="/register"
            className="rounded-md bg-brand-500 px-3 py-1.5 font-medium text-white shadow-sm shadow-brand-900/40 transition-all duration-200 hover:bg-brand-600 hover:shadow-md hover:shadow-brand-600/30 active:scale-95 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
          >
            Register
          </a>
        </nav>
      )}
    </header>
  );
}
