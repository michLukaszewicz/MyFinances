import { useNavigate } from "react-router";
import { apiFetch } from "../lib/api";
import logo from "../assets/myfinances-logo.png";

interface AppHeaderProps {
  showLogout: boolean;
}

export function AppHeader({ showLogout }: AppHeaderProps) {
  const navigate = useNavigate();

  async function handleLogout() {
    await apiFetch("/auth/logout", { method: "POST" });
    navigate("/login");
  }

  return (
    <header className="flex items-center justify-between p-4">
      <img src={logo} alt="MyFinances" className="h-7 w-auto" />
      {showLogout && (
        <button
          type="button"
          onClick={handleLogout}
          className="text-sm text-brand-400 hover:text-brand-300 hover:underline"
        >
          Log out
        </button>
      )}
    </header>
  );
}
