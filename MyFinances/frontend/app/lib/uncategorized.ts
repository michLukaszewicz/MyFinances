import { useEffect, useState } from "react";
import { apiFetch } from "./api";

const CHANGED_EVENT = "uncategorized:changed";

// Mirrors the backend's CategorizationContracts.cs UncategorizedCountDto.
interface UncategorizedCountDto {
  count: number;
}

// Survives client-side navigation (every route renders its own header, so the header remounts on
// each page change) but not a full page load, so the dot does not flash empty on navigation while
// a fresh load re-announces what is waiting.
let lastKnownCount = 0;

// Tell every mounted header to re-read the count after something changed the queue (a
// categorization, an import, a deleted category).
export function notifyUncategorizedChanged() {
  window.dispatchEvent(new Event(CHANGED_EVENT));
}

export function useUncategorizedCount(enabled: boolean): number {
  const [count, setCount] = useState(lastKnownCount);

  useEffect(() => {
    if (!enabled) {
      lastKnownCount = 0;
      return;
    }
    let cancelled = false;

    async function refresh() {
      try {
        const result = await apiFetch<UncategorizedCountDto>("/categorization/queue/count");
        lastKnownCount = result.count;
        if (!cancelled) setCount(result.count);
      } catch {
        // The indicator is a hint; a failed refresh keeps the last known value.
      }
    }

    void refresh();
    window.addEventListener(CHANGED_EVENT, refresh);
    window.addEventListener("focus", refresh);
    return () => {
      cancelled = true;
      window.removeEventListener(CHANGED_EVENT, refresh);
      window.removeEventListener("focus", refresh);
    };
  }, [enabled]);

  return enabled ? count : 0;
}
