import { useState } from "react";
import { redirect, useNavigate } from "react-router";
import { apiFetch, ApiError } from "../lib/api";
import { AppHeader } from "../components/AppHeader";

export async function clientLoader() {
  const res = await fetch("/api/auth/me", { credentials: "include" });
  if (res.ok) throw redirect("/");
  return null;
}

async function extractErrorMessage(error: unknown): Promise<string> {
  if (error instanceof ApiError) {
    try {
      const body = await error.response.json();
      if (typeof body?.title === "string") {
        return body.title;
      }
    } catch {
      // fall through to generic message
    }
  }
  return "Something went wrong. Please try again.";
}

export default function Register() {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await apiFetch("/auth/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });
      navigate("/");
    } catch (err) {
      setError(await extractErrorMessage(err));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <>
      <AppHeader authenticated={false} />
      <main className="flex items-center justify-center pb-4">
        <div className="max-w-[300px] w-full space-y-6 px-4">
          <h1
            className="text-center text-lg font-semibold text-gray-200 animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "0ms" }}
          >
            Register
          </h1>
          <form
            onSubmit={handleSubmit}
            className="space-y-4 animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "50ms" }}
          >
            <div className="space-y-1">
              <label htmlFor="email" className="text-sm text-gray-200">
                Email
              </label>
              <input
                id="email"
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="w-full rounded-lg border border-gray-700 bg-transparent p-2 text-sm text-gray-200 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
              />
            </div>
            <div className="space-y-1">
              <label htmlFor="password" className="text-sm text-gray-200">
                Password
              </label>
              <input
                id="password"
                type="password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="w-full rounded-lg border border-gray-700 bg-transparent p-2 text-sm text-gray-200 focus:border-brand-500 focus:outline-none focus:ring-1 focus:ring-brand-500"
              />
            </div>
            {error && <p className="text-sm text-red-600">{error}</p>}
            <button
              type="submit"
              disabled={submitting}
              className="w-full rounded-lg bg-brand-500 p-2 text-sm font-medium text-white transition-colors duration-200 hover:bg-brand-600 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-500"
            >
              {submitting ? "Registering…" : "Register"}
            </button>
          </form>
          <p
            className="text-center text-sm text-gray-500 animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "100ms" }}
          >
            Already have an account?{" "}
            <a href="/login" className="text-brand-400 hover:text-brand-300 hover:underline">
              Log in
            </a>
          </p>
        </div>
      </main>
    </>
  );
}
