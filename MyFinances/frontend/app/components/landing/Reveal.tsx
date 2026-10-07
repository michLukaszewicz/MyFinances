import type { ReactNode } from "react";
import { cn } from "../../lib/utils";
import { revealDelayStyle, useReveal } from "../../lib/useReveal";

interface RevealProps {
  children: ReactNode;
  className?: string;
  delayMs?: number;
}

export function Reveal({ children, className, delayMs = 0 }: RevealProps) {
  const { ref, revealed } = useReveal<HTMLDivElement>();

  return (
    <div
      ref={ref}
      className={cn("reveal", className)}
      data-revealed={revealed || undefined}
      style={revealDelayStyle(delayMs)}
    >
      {children}
    </div>
  );
}
