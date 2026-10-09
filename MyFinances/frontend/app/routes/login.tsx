import { useState } from "react";
import { redirect, useNavigate } from "react-router";
import { apiFetch, ApiError } from "../lib/api";
import { AppHeader } from "../components/AppHeader";
import { Button } from "../components/ui/button";
import { Card, CardContent } from "../components/ui/card";
import { Input } from "../components/ui/input";

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

const linkClassName =
  "rounded-sm text-primary underline-offset-4 outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background";

export default function Login() {
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
      await apiFetch("/auth/login", {
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
      <main className="flex items-center justify-center pt-16 pb-4">
        <div className="max-w-sm w-full space-y-6 px-4">
          <h1
            className="text-center text-lg font-semibold text-foreground animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "0ms" }}
          >
            Log in
          </h1>
          <Card className="animate-[fade-slide-in_600ms_ease-out_both]" style={{ animationDelay: "50ms" }}>
            <CardContent>
              <form onSubmit={handleSubmit} className="space-y-4">
                <div className="space-y-1">
                  <label htmlFor="email" className="text-sm text-foreground">
                    Email
                  </label>
                  <Input
                    id="email"
                    type="email"
                    required
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                  />
                </div>
                <div className="space-y-1">
                  <label htmlFor="password" className="text-sm text-foreground">
                    Password
                  </label>
                  <Input
                    id="password"
                    type="password"
                    required
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                  />
                </div>
                {error && <p className="text-sm text-destructive">{error}</p>}
                <Button type="submit" disabled={submitting} className="w-full">
                  {submitting ? "Logging in…" : "Log in"}
                </Button>
              </form>
            </CardContent>
          </Card>
          <p
            className="text-center text-sm text-muted-foreground animate-[fade-slide-in_600ms_ease-out_both]"
            style={{ animationDelay: "100ms" }}
          >
            No account?{" "}
            <a href="/register" className={linkClassName}>
              Register
            </a>
          </p>
        </div>
      </main>
    </>
  );
}
