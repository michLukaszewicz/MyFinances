import type { Config } from "@react-router/dev/config";

export default {
  // Config options...
  // This frontend is a pure client SPA served as static files by the .NET API
  // (see MyFinances/backend/Program.cs) — no Node SSR server in this stack.
  ssr: false,
} satisfies Config;
