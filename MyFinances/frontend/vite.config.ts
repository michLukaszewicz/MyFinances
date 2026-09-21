import { reactRouter } from "@react-router/dev/vite";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [tailwindcss(), reactRouter()],
  resolve: {
    tsconfigPaths: true,
  },
  server: {
    proxy: {
      // The backend owns the /api prefix itself (see MyFinances/backend/Program.cs),
      // so this just forwards unchanged — no rewrite needed.
      "/api": {
        target: "http://localhost:5007",
        changeOrigin: true,
      },
    },
  },
});
