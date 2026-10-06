import { Link, useNavigate } from "react-router";
import { apiFetch } from "../lib/api";
import logo from "../assets/myfinances-logo.png";
import { Button } from "./ui/button";

const navButtonClass = "h-auto px-3 py-1.5";

const navLinks = [
  { to: "/", label: "Dashboard" },
  { to: "/import", label: "Import transactions" },
  { to: "/categorize", label: "Categorize" },
  { to: "/settings", label: "Settings" },
];

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
    <header className="sticky top-0 z-50 flex items-center justify-between border-b border-border bg-background/70 px-6 py-4 backdrop-blur-md">
      <div className="flex items-center gap-6">
        <Link to="/" aria-label="MyFinances home">
          <img src={logo} alt="MyFinances" className="h-7 w-auto" />
        </Link>
        {authenticated && (
          <nav className="flex items-center gap-2 text-sm">
            {navLinks.map((item) => (
              <Button key={item.to} asChild variant="ghost" className={navButtonClass}>
                <Link to={item.to}>{item.label}</Link>
              </Button>
            ))}
          </nav>
        )}
      </div>
      {authenticated ? (
        <nav className="flex items-center gap-2 text-sm">
          <Button type="button" variant="ghost" onClick={handleLogout} className={navButtonClass}>
            Log out
          </Button>
        </nav>
      ) : (
        <nav className="flex items-center gap-2 text-sm">
          <Button asChild variant="ghost" className={navButtonClass}>
            <a href="/login">Log in</a>
          </Button>
          <Button asChild className={navButtonClass}>
            <a href="/register">Register</a>
          </Button>
        </nav>
      )}
    </header>
  );
}
