import { Link } from "react-router";
import { Button } from "../ui/button";
import { ProductMock } from "./ProductMock";

export function Hero() {
  return (
    <section className="relative flex min-h-[calc(100svh-4.5rem)] flex-col items-center justify-center px-4 py-16 text-center">
      <div
        className="pointer-events-none absolute inset-0 -z-30 bg-[length:200%_200%] opacity-20 animate-[gradient-pan_18s_ease-in-out_infinite]"
        style={{
          backgroundImage:
            "radial-gradient(circle at 20% 20%, var(--color-primary) 0%, transparent 45%), radial-gradient(circle at 80% 30%, var(--color-secondary) 0%, transparent 40%), radial-gradient(circle at 50% 80%, var(--color-primary) 0%, transparent 45%)",
        }}
        aria-hidden="true"
      />
      <div className="starfield starfield-near pointer-events-none absolute inset-0 -z-20" aria-hidden="true" />
      <div className="starfield starfield-far pointer-events-none absolute inset-0 -z-20" aria-hidden="true" />
      <div
        className="pointer-events-none absolute -left-24 top-10 -z-10 h-72 w-72 rounded-full bg-primary opacity-20 blur-3xl animate-[drift-slow_14s_ease-in-out_infinite]"
        aria-hidden="true"
      />
      <div
        className="pointer-events-none absolute -right-24 bottom-0 -z-10 h-80 w-80 rounded-full bg-secondary opacity-20 blur-3xl animate-[drift-slow-reverse_16s_ease-in-out_infinite]"
        aria-hidden="true"
      />
      <div className="flex max-w-3xl flex-col items-center gap-6">
        <h1
          className="text-4xl font-semibold tracking-tight text-foreground animate-[fade-slide-in_600ms_ease-out_both] sm:text-5xl lg:text-6xl"
          style={{ animationDelay: "0ms" }}
        >
          Compare your spend with your own <span className="text-primary">history</span>
        </h1>
        <p
          className="max-w-xl text-base text-muted-foreground animate-[fade-slide-in_600ms_ease-out_both] sm:text-lg"
          style={{ animationDelay: "100ms" }}
        >
          Import your bank statements, categorize your transactions, and see each category
          against your own past averages. No budget required.
        </p>
        <div
          className="flex flex-wrap justify-center gap-3 animate-[fade-slide-in_600ms_ease-out_both]"
          style={{ animationDelay: "200ms" }}
        >
          <Button asChild className="h-11 px-6 text-base">
            <Link to="/register">Register</Link>
          </Button>
          <Button asChild variant="outline" className="h-11 px-6 text-base">
            <Link to="/login">Log in</Link>
          </Button>
        </div>
      </div>
      <div className="mt-12 w-full max-w-3xl" data-slot="hero-mock">
        <ProductMock />
      </div>
    </section>
  );
}
