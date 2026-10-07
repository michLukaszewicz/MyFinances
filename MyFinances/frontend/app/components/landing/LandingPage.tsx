import { useEffect, useState } from "react";
import { cn } from "../../lib/utils";
import { BanksStrip } from "./BanksStrip";
import { Features } from "./Features";
import { Hero } from "./Hero";
import { HowItWorks } from "./HowItWorks";

export function LandingPage() {
  const [revealReady, setRevealReady] = useState(false);

  useEffect(() => {
    setRevealReady(true);
  }, []);

  return (
    <main className={cn("relative overflow-hidden", revealReady && "reveal-ready")}>
      <Hero />
      <BanksStrip />
      <Features />
      <HowItWorks />
    </main>
  );
}
