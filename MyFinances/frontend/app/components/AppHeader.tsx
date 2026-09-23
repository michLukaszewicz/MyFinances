import { useNavigate } from "react-router";
import { apiFetch } from "../lib/api";

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
      <span className="text-sm font-semibold text-gray-700 dark:text-gray-200">
        MyFinances
      </span>
      {showLogout && (
        <button
          type="button"
          onClick={handleLogout}
          className="text-sm text-blue-700 hover:underline dark:text-blue-500"
        >
          Log out
        </button>
      )}
    </header>
  );
}
