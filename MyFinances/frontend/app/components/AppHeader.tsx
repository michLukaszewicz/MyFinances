import { useEffect } from "react";
import { Link, useLocation, useNavigate } from "react-router";
import { toast } from "sonner";
import { apiFetch } from "../lib/api";
import { useUncategorizedCount } from "../lib/uncategorized";
import logo from "../assets/myfinances-logo.png";
import { Button } from "./ui/button";

const navButtonClass = "h-auto px-3 py-1.5";
const UNCATEGORIZED_TOAST_ID = "uncategorized-transactions";

// The count last announced in a toast. Module-level so navigating between pages does not repeat
// the toast, while a fresh page load or a higher count (new import) announces again.
let lastAnnouncedCount = 0;

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
  const { pathname } = useLocation();
  const uncategorizedCount = useUncategorizedCount(authenticated);

  useEffect(() => {
    if (!authenticated) {
      lastAnnouncedCount = 0;
      return;
    }
    const alreadyOnCategorizePage = pathname === "/categorize";
    if (uncategorizedCount > lastAnnouncedCount && !alreadyOnCategorizePage) {
      toast.info(
        `${uncategorizedCount} ${uncategorizedCount === 1 ? "transaction needs" : "transactions need"} a category`,
        {
          id: UNCATEGORIZED_TOAST_ID,
          duration: 8000,
          action: { label: "Categorize", onClick: () => navigate("/categorize") },
        },
      );
    }
    if (uncategorizedCount === 0 || !alreadyOnCategorizePage) {
      lastAnnouncedCount = uncategorizedCount;
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [authenticated, uncategorizedCount, pathname]);

  async function handleLogout() {
    try {
      await apiFetch("/auth/logout", { method: "POST" });
    } finally {
      navigate("/login");
    }
  }

  return (
    <header className="sticky top-0 z-50 flex flex-wrap items-center justify-between gap-y-2 border-b border-border bg-background/70 px-6 py-4 backdrop-blur-md">
      <div className="flex flex-wrap items-center gap-x-6 gap-y-2">
        <Link to="/" aria-label="MyFinances home" className="shrink-0">
          <img src={logo} alt="MyFinances" className="h-7 w-auto max-w-none shrink-0" />
        </Link>
        {authenticated && (
          <nav className="flex flex-wrap items-center gap-2 text-sm">
            {navLinks.map((item) => (
              <Button key={item.to} asChild variant="ghost" className={navButtonClass}>
                <Link to={item.to} className="relative">
                  {item.label}
                  {item.to === "/categorize" && uncategorizedCount > 0 && (
                    <>
                      <span
                        aria-hidden="true"
                        title={`${uncategorizedCount} uncategorized`}
                        className="absolute -right-2 -top-0.5 size-2 rounded-full bg-primary"
                      />
                      <span className="sr-only">
                        ({uncategorizedCount} uncategorized {uncategorizedCount === 1 ? "transaction" : "transactions"})
                      </span>
                    </>
                  )}
                </Link>
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
