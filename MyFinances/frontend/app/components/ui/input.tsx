import * as React from "react";

import { cn } from "~/lib/utils";

// Shared control styling. Exported so native <select> elements (and any other
// bare control) match Input without pulling in a Radix Select.
const inputClassName =
  "h-9 w-full min-w-0 rounded-md border border-input bg-transparent px-3 py-1 text-sm text-foreground shadow-xs transition-colors outline-none placeholder:text-muted-foreground selection:bg-primary selection:text-primary-foreground focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-destructive";

const selectClassName = cn(inputClassName, "bg-background pr-8");

function Input({ className, type, ...props }: React.ComponentProps<"input">) {
  return (
    <input
      type={type}
      data-slot="input"
      className={cn(inputClassName, className)}
      {...props}
    />
  );
}

export { Input, inputClassName, selectClassName };
