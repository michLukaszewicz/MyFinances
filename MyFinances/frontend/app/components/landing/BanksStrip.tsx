import { Reveal } from "./Reveal";

const BANKS = [
  { name: "mBank", formats: "CSV and PDF" },
  { name: "Erste Bank Polska", formats: "CSV and PDF" },
  { name: "VeloBank", formats: "PDF" },
];

export function BanksStrip() {
  return (
    <section className="px-4 py-16">
      <Reveal className="mx-auto flex max-w-3xl flex-col items-center gap-6 text-center">
        <h2 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
          Import statements from your bank
        </h2>
        <ul className="flex flex-wrap justify-center gap-3">
          {BANKS.map((bank) => (
            <li
              key={bank.name}
              className="rounded-full border border-border bg-card px-4 py-2 text-sm text-foreground"
            >
              <span className="font-medium">{bank.name}</span>
              <span className="text-muted-foreground"> · {bank.formats}</span>
            </li>
          ))}
        </ul>
        <p className="text-sm text-muted-foreground">Works with PLN accounts.</p>
      </Reveal>
    </section>
  );
}
