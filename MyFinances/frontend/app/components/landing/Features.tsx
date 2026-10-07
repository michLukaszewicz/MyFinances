import { Card, CardContent, CardDescription, CardHeader } from "../ui/card";
import { Reveal } from "./Reveal";

const FEATURES = [
  {
    title: "Import and review duplicates",
    description:
      "Upload mBank or Erste CSV and PDF statements, or VeloBank PDFs. Possible duplicates are shown side by side so you decide what to keep.",
  },
  {
    title: "Categorize in minutes",
    description:
      "Work through a simple queue and choose a category for each transaction yourself. Transfers between your own accounts are flagged for you.",
  },
  {
    title: "Spend vs. your own history",
    description:
      "Spend and income donuts and a trend chart show where money goes, and every category is marked above, below or in line with your average.",
  },
];

export function Features() {
  return (
    <section className="px-4 py-16">
      <div className="mx-auto max-w-5xl">
        <Reveal className="mb-10 text-center">
          <h2 className="text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
            Everything you need to understand your spending
          </h2>
        </Reveal>
        <div className="grid gap-6 md:grid-cols-3">
          {FEATURES.map((feature, index) => (
            <Reveal key={feature.title} delayMs={index * 120}>
              <Card className="h-full transition-transform duration-200 hover:-translate-y-1 motion-reduce:transition-none">
                <CardHeader>
                  <h3 className="text-lg font-semibold leading-tight text-card-foreground">{feature.title}</h3>
                </CardHeader>
                <CardContent>
                  <CardDescription className="text-base">{feature.description}</CardDescription>
                </CardContent>
              </Card>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
