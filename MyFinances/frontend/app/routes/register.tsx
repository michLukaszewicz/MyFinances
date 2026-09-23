import { useState } from "react";
import { redirect, useNavigate } from "react-router";

export async function clientLoader() {
  const res = await fetch("/api/auth/me");
  if (res.ok) throw redirect("/");
  return null;
}

async function extractErrorMessage(response: Response): Promise<string> {
  try {
    const body = await response.json();
    if (typeof body?.title === "string") {
      return body.title;
    }
  } catch {
    // fall through to generic message
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
      const response = await fetch("/api/auth/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });
      if (!response.ok) {
        setError(await extractErrorMessage(response));
        return;
      }
      navigate("/");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <main className="flex items-center justify-center pt-16 pb-4">
      <div className="max-w-[300px] w-full space-y-6 px-4">
        <h1 className="text-center text-lg font-semibold text-gray-700 dark:text-gray-200">
          Register
        </h1>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-1">
            <label htmlFor="email" className="text-sm text-gray-700 dark:text-gray-200">
              Email
            </label>
            <input
              id="email"
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="w-full rounded-lg border border-gray-200 p-2 text-sm dark:border-gray-700 dark:bg-transparent dark:text-gray-200"
            />
          </div>
          <div className="space-y-1">
            <label htmlFor="password" className="text-sm text-gray-700 dark:text-gray-200">
              Password
            </label>
            <input
              id="password"
              type="password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full rounded-lg border border-gray-200 p-2 text-sm dark:border-gray-700 dark:bg-transparent dark:text-gray-200"
            />
          </div>
          {error && <p className="text-sm text-red-600">{error}</p>}
          <button
            type="submit"
            disabled={submitting}
            className="w-full rounded-lg border border-gray-200 p-2 text-sm text-blue-700 hover:underline disabled:opacity-50 dark:border-gray-700 dark:text-blue-500"
          >
            {submitting ? "Registering…" : "Register"}
          </button>
        </form>
        <p className="text-center text-sm text-gray-500">
          Already have an account?{" "}
          <a href="/login" className="text-blue-700 hover:underline dark:text-blue-500">
            Log in
          </a>
        </p>
      </div>
    </main>
  );
}
