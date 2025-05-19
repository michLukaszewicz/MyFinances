import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      "/testdata": {
        target: "https://localhost:7121",
        changeOrigin: true,
        secure: false, 
      },
    },
  },
});
