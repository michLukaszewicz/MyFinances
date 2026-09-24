import { useLoaderData } from "react-router";
import type { Route } from "./+types/home";
import { AppHeader } from "../components/AppHeader";
import logo from "../assets/myfinances-logo.png";

const valueProps = [
  {
    title: "Import your statements",
    description: "Upload bank CSV exports from mBank, Revolut, and Erste.",
  },
  {
    title: "Categorize in minutes",
    description: "Tag transactions and let duplicates and transfers surface themselves.",
  },
  {
    title: "See spend vs. your own history",
    description: "Compare each category against your own past averages, not a stranger's budget.",
  },
];

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
      <main className="relative flex min-h-screen items-center justify-center overflow-hidden pt-16 pb-4">
        <div
          className="pointer-events-none absolute inset-0 -z-20 bg-[length:200%_200%] opacity-60 animate-[gradient-pan_18s_ease-in-out_infinite]"
          style={{
            backgroundImage:
              "radial-gradient(circle at 20% 20%, var(--color-brand-900) 0%, transparent 45%), radial-gradient(circle at 80% 30%, var(--color-brand-800) 0%, transparent 40%), radial-gradient(circle at 50% 80%, var(--color-brand-900) 0%, transparent 45%)",
          }}
          aria-hidden="true"
        />
        <div
          className="pointer-events-none absolute -left-24 top-10 -z-10 h-72 w-72 rounded-full bg-brand-600 opacity-30 blur-3xl animate-[drift-slow_14s_ease-in-out_infinite]"
          aria-hidden="true"
        />
        <div
          className="pointer-events-none absolute -right-24 bottom-0 -z-10 h-80 w-80 rounded-full bg-brand-400 opacity-20 blur-3xl animate-[drift-slow-reverse_16s_ease-in-out_infinite]"
          aria-hidden="true"
        />
        <div className="max-w-lg w-full space-y-8 px-4 text-center">
          <div
            className="relative mx-auto flex w-full max-w-xs items-center justify-center animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "0ms" }}
          >
            <div
              className="absolute inset-0 -z-10 rounded-full bg-brand-500 opacity-50 blur-3xl animate-[ambient-glow_5s_ease-in-out_infinite]"
              aria-hidden="true"
            />
            <img
              src={logo}
              alt="MyFinances"
              className="w-full h-auto drop-shadow-[0_0_25px_rgba(36,19,222,0.45)]"
            />
          </div>
          <h1 className="sr-only">MyFinances</h1>
          <p
            className="text-sm text-gray-400 animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "100ms" }}
          >
            Categorize your own spend and compare it to your own history — no
            budget required up front.
          </p>
          <div
            className="flex justify-center gap-4 text-sm animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "200ms" }}
          >
            <a
              href="/login"
              className="rounded-md bg-brand-500 px-4 py-2 font-medium text-white shadow-md shadow-brand-900/40 transition-all duration-200 hover:scale-105 hover:bg-brand-600 hover:shadow-lg hover:shadow-brand-600/40 active:scale-95 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
            >
              Log in
            </a>
            <a
              href="/register"
              className="rounded-md px-4 py-2 font-medium text-brand-400 ring-1 ring-inset ring-brand-400 transition-all duration-200 hover:scale-105 hover:bg-brand-900/30 hover:shadow-lg hover:shadow-brand-600/20 active:scale-95 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
            >
              Register
            </a>
          </div>
          <div
            className="grid grid-cols-1 gap-4 pt-4 text-left sm:grid-cols-3 animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "300ms" }}
          >
            {valueProps.map((item) => (
              <div
                key={item.title}
                className="space-y-1 rounded-lg border border-gray-800 p-3 transition-all duration-200 hover:-translate-y-1 hover:border-brand-500/60 hover:bg-brand-900/30 hover:shadow-lg hover:shadow-brand-900/30"
              >
                <h2 className="text-sm font-semibold text-gray-200">
                  {item.title}
                </h2>
                <p className="text-xs text-gray-400">{item.description}</p>
              </div>
            ))}
          </div>
        </div>
      </main>
    );
  }

  return (
    <>
      <AppHeader showLogout />
      <main className="relative flex items-center justify-center overflow-hidden pt-16 pb-4">
        <div
          className="pointer-events-none absolute left-1/2 top-16 -z-10 h-56 w-56 -translate-x-1/2 rounded-full bg-brand-600 opacity-20 blur-3xl animate-[ambient-glow_6s_ease-in-out_infinite]"
          aria-hidden="true"
        />
        <div className="max-w-[300px] w-full space-y-6 px-4 text-center">
          <h1
            className="text-lg font-semibold text-gray-200 animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "0ms" }}
          >
            Welcome back, {user.email}
          </h1>
          <p
            className="text-sm text-gray-500 animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "100ms" }}
          >
            Once bank import lands, you'll see your categorized spend here.
          </p>
        </div>
      </main>
    </>
  );
}
