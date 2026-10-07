import { Reveal } from "./Reveal";

const STEPS = [
  { title: "Import", description: "Upload a statement from mBank, Erste Bank Polska or VeloBank." },
  { title: "Categorize", description: "Assign a category to each transaction. Internal transfers are flagged." },
  { title: "Compare", description: "See each category against your own past averages." },
];

export function HowItWorks() {
  return (
    <section className="px-4 py-16">
      <div className="mx-auto max-w-4xl">
        <Reveal className="mb-10 text-center">
          <h2 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">How it works</h2>
        </Reveal>
        <ol className="grid gap-8 md:grid-cols-3">
          {STEPS.map((step, index) => (
            <li key={step.title}>
              <Reveal delayMs={index * 120} className="flex flex-col items-center gap-3 text-center md:items-start md:text-left">
                <span className="flex h-10 w-10 items-center justify-center rounded-full bg-primary text-base font-semibold text-primary-foreground">
                  {index + 1}
                </span>
                <h3 className="text-lg font-semibold text-foreground">{step.title}</h3>
                <p className="text-muted-foreground">{step.description}</p>
              </Reveal>
            </li>
          ))}
        </ol>
      </div>
    </section>
  );
}
