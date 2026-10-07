import { useCallback, useEffect, useRef, useState } from "react";

const callbacks = new Map<Element, () => void>();
let sharedObserver: IntersectionObserver | null = null;

function getObserver(): IntersectionObserver {
  if (sharedObserver === null) {
    sharedObserver = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (!entry.isIntersecting) continue;
          const callback = callbacks.get(entry.target);
          sharedObserver?.unobserve(entry.target);
          callbacks.delete(entry.target);
          callback?.();
        }
      },
      { threshold: 0.15, rootMargin: "0px 0px -8% 0px" },
    );
  }
  return sharedObserver;
}

function stopObserving(element: Element) {
  if (!callbacks.delete(element)) return;
  sharedObserver?.unobserve(element);
  if (callbacks.size === 0) {
    sharedObserver?.disconnect();
    sharedObserver = null;
  }
}

function shouldRevealImmediately(): boolean {
  return (
    typeof IntersectionObserver === "undefined" ||
    window.matchMedia("(prefers-reduced-motion: reduce)").matches
  );
}

// Sets `revealed` once the element first scrolls into view. Pair with the `.reveal` CSS class and
// `data-revealed={revealed || undefined}`; use `revealDelayStyle(ms)` for staggered items.
export function useReveal<T extends HTMLElement>(): {
  ref: (node: T | null) => void;
  revealed: boolean;
} {
  const [revealed, setRevealed] = useState(false);
  const elementRef = useRef<T | null>(null);

  const ref = useCallback((node: T | null) => {
    if (elementRef.current) {
      stopObserving(elementRef.current);
    }
    elementRef.current = node;
    if (!node) return;
    if (shouldRevealImmediately()) {
      setRevealed(true);
      return;
    }
    callbacks.set(node, () => setRevealed(true));
    getObserver().observe(node);
  }, []);

  useEffect(
    () => () => {
      if (elementRef.current) {
        stopObserving(elementRef.current);
      }
    },
    [],
  );

  return { ref, revealed };
}

export function revealDelayStyle(delayMs: number): React.CSSProperties {
  return { "--reveal-delay": `${delayMs}ms` } as React.CSSProperties;
}
