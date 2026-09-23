import { useLoaderData } from "react-router";
import type { Route } from "./+types/home";
import { AppHeader } from "../components/AppHeader";

export function meta({}: Route.MetaArgs) {
  return [
    { title: "MyFinances" },
    {
      name: "description",
      content:
        "Categorize your own spend and compare it to your own history — no budget required up front.",
    },
  ];
}

export async function clientLoader() {
  const res = await fetch("/api/auth/me");
  if (!res.ok) return null;
  return res.json();
}

export default function Home() {
  const user = useLoaderData<typeof clientLoader>();

  if (!user) {
    return (
      <main className="flex items-center justify-center pt-16 pb-4">
        <div className="max-w-[300px] w-full space-y-6 px-4 text-center">
          <h1 className="text-lg font-semibold text-gray-700 dark:text-gray-200">
            MyFinances
          </h1>
          <p className="text-sm text-gray-500">
            Categorize your own spend and compare it to your own history — no
            budget required up front.
          </p>
          <div className="flex justify-center gap-4 text-sm">
            <a
              href="/login"
              className="text-blue-700 hover:underline dark:text-blue-500"
            >
              Log in
            </a>
            <a
              href="/register"
              className="text-blue-700 hover:underline dark:text-blue-500"
            >
              Register
            </a>
          </div>
        </div>
      </main>
    );
  }

  return (
    <>
      <AppHeader showLogout />
      <main className="flex items-center justify-center pt-16 pb-4">
        <div className="max-w-[300px] w-full space-y-6 px-4 text-center">
          <h1 className="text-lg font-semibold text-gray-700 dark:text-gray-200">
            Welcome back, {user.email}
          </h1>
          <p className="text-sm text-gray-500">
            Once bank import lands, you'll see your categorized spend here.
          </p>
        </div>
      </main>
    </>
  );
}
