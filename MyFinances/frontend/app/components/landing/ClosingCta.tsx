import { Link } from "react-router";
import { Button } from "../ui/button";
import { Reveal } from "./Reveal";

export function ClosingCta() {
  return (
    <>
      <section className="px-4 py-20">
        <Reveal className="mx-auto flex max-w-2xl flex-col items-center gap-6 text-center">
          <h2 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
            See where your money really goes
          </h2>
          <p className="text-muted-foreground">
            Import a statement, categorize your transactions and compare every category with your own history.
          </p>
          <div className="flex flex-wrap justify-center gap-3">
            <Button asChild className="h-11 px-6 text-base">
              <Link to="/register">Register</Link>
            </Button>
            <Button asChild variant="outline" className="h-11 px-6 text-base">
              <Link to="/login">Log in</Link>
            </Button>
          </div>
        </Reveal>
      </section>
      <footer className="border-t border-border px-4 py-6 text-center text-sm text-muted-foreground">
        MyFinances &copy; {new Date().getFullYear()}
      </footer>
    </>
  );
}
