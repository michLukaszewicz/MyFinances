const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "/api";

const MUTATING_METHODS = new Set(["POST", "PUT", "PATCH", "DELETE"]);

// ASP.NET Core's antiforgery double-submit pattern pairs a server-set cookie
// (sent automatically by the browser) with a distinct "request token" value
// that must be echoed back as a header — the two values are NOT the same
// string, so this can't be read off the cookie. A token fetched once and
// cached (e.g. at app bootstrap, before the user logs in) was observed to
// go stale by the time it's used after sign-in, so instead fetch a fresh
// token immediately before every mutating request rather than caching one.
async function fetchXsrfToken(): Promise<string> {
  const res = await fetch(`${API_BASE_URL}/auth/antiforgery-token`);
  const { token } = (await res.json()) as { token: string };
  return token;
}

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const method = init?.method?.toUpperCase() ?? "GET";
  const headers = new Headers(init?.headers);

  if (MUTATING_METHODS.has(method)) {
    headers.set("X-XSRF-TOKEN", await fetchXsrfToken());
  }

  const response = await fetch(`${API_BASE_URL}${path}`, { ...init, headers });
  if (!response.ok) {
    throw new Error(`API request to ${path} failed with status ${response.status}`);
  }
  // Some endpoints (e.g. logout) return 200 with an empty body — response.json()
  // throws a SyntaxError on empty input, so only parse when there's content.
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}
