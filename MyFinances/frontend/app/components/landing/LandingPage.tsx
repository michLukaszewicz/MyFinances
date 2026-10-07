import { useEffect, useState } from "react";
import { cn } from "../../lib/utils";
import { Hero } from "./Hero";

export function LandingPage() {
  const [revealReady, setRevealReady] = useState(false);

  useEffect(() => {
    setRevealReady(true);
  }, []);

  return (
    <main className={cn("relative overflow-hidden", revealReady && "reveal-ready")}>
      <Hero />
    </main>
  );
}
