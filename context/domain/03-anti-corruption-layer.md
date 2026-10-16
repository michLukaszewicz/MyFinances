---
title: MyFinances — Anti-Corruption Layer wokół PdfPig (plan refaktoru)
created: 2026-10-16
type: refactor-plan
---

# 03 — Anti-Corruption Layer: PdfPig

Plan, nie implementacja. Kod produkcyjny nie był zmieniany. Wszystkie `plik:linia` zweryfikowane na `main` (42124e2); ścieżki względem `MyFinances/backend` (dalej `BE`) i `MyFinances/frontend` (`FE`). Poprzednie etapy: [01-domain-distillation.md](01-domain-distillation.md), [02-invariant-aggregate-refactor.md](02-invariant-aggregate-refactor.md), słownik: [glossary.md](glossary.md).

## 0. Kontekst

**Stack** (`BE/MyFinances.Api.csproj:4,20-30`): net10.0, CsvHelper 33.1.0, ASP.NET Identity + EF Core + Npgsql 10.0.3, **PdfPig 0.1.16**, Swashbuckle, System.Text.Encoding.CodePages. Testy: xUnit, `Microsoft.AspNetCore.Mvc.Testing`, EF InMemory (`BE/Tests/MyFinances.Api.Tests.csproj:13-17`). Frontend (`FE/package.json`): react-router 8, recharts 3.10, sonner 2, radix/shadcn, tailwind.

**Warstwy backendu** (katalogi pod `BE/`): `Import/` (parsery, endpointy importu), `Transactions/`, `Categorization/`, `Dashboard/`, `Auth/`, `DI/`, `Migrations/`. Brak osobnych projektów — granice są katalogowe i przez interfejs `IBankStatementParser` (`BE/Import/IBankStatementParser.cs:14`), rejestrowany w `BE/DI/ImportServiceCollectionExtensions.cs:12-16`.

**Deklaracje o wymienialności.** Żaden dokument nie mówi wprost „PdfPig ma być wymienialny”. Są dwie deklaracje pośrednie, o izolacji:

- `context/archive/2026-10-02-pdf-statement-import/research.md:167`: „The `IBankStatementParser` design isolates every format quirk behind `Stream` in, `NormalizedTransaction` out. A PDF parser is the same kind of thing as a CSV parser with different internals (PdfPig instead of CsvHelper)”.
- `context/archive/2026-10-02-pdf-import/plan.md:126`: „One place for the PdfPig access pattern and the page model every PDF parser consumes, so the three banks cannot drift on error handling.”
- Kod to powtarza: `BE/Import/Pdf/PdfStatementReader.cs:29` — „Everything that touches PdfPig sits behind this one catch-all”.

Wniosek: intencja to *jedno miejsce, które zna PdfPig*. Kod jej nie dotrzymuje w jednym, konkretnym punkcie (sekcja 3). Nie wolno więc pisać, że dokumenty „obiecują wymienialność” — obiecują izolację, a rozjazd jest węższy niż w typowym przecieku.

## 1. Zależności zewnętrzne i kto je zna

| # | Zależność | Pliki produkcyjne, które ją znają | Przecieka przez granicę? |
|---|---|---|---|
| A | **PdfPig** (`UglyToad.PdfPig*`) | `BE/Import/Pdf/PdfStatementReader.cs:2,3,12,67,91,93-107`; `BE/Import/ErstePdfParser.cs:5`; `BE/Import/MBankPdfParser.cs:4`; `BE/Import/VeloBankPdfParser.cs:4` (4 pliki) | **Tak** — typ biblioteki w publicznym modelu i w ~30 sygnaturach/lambdach parserów |
| B | CsvHelper | `BE/Import/ErsteCsvParser.cs:3-4,36,64,74,111,151`; `BE/Import/MBankCsvParser.cs:3-4,143,151` (2 pliki) | Częściowo — `CsvReader`/`CsvConfiguration` w prywatnych metodach, nie w kontrakcie; ale rekonstrukcja konfiguracji zduplikowana w 2 parserach |
| C | ASP.NET Identity (`UserManager<AppUser>`) | `BE/Transactions/ClaimsPrincipalExtensions.cs:11-13` + 7 plików endpointów (`CategorizationEndpoints` 14 użyć, `AccountEndpoints` 8, `TransactionEndpoints` 8, `DashboardEndpoints` 4, `ImportEndpoints` 4, `CategoryTrendEndpoints` 2, `AuthEndpoints` 1) | Częściowo — endpoint zna `UserManager`, ale wyłącznie by wyłuskać `Guid`; ujednolicone jednym rozszerzeniem |
| D | EF Core (`AppDbContext`) | 18 plików produkcyjnych poza migracjami | Tak, ale celowo (brak repozytorium); adresuje je plan 02 (`ITransactionRepository`) |
| E | Recharts | `FE/app/components/CategorySpendDonut.tsx:3`, `FE/app/components/CategoryTrendChart.tsx:2` (2 pliki) | Nie — jedna warstwa (UI), dwa komponenty |
| F | Sonner | `FE/app/components/AppHeader.tsx:3`, `FE/app/components/ui/sonner.tsx:8` | Nie — jedna warstwa; shadcn wrapper |
| G | System.Text.Encoding.CodePages | `BE/Program.cs:16`, `BE/Import/MBankCsvParser.cs:24-29` | Rejestracja w dwóch miejscach (celowa, opisana komentarzem `MBankCsvParser.cs:21`) |

Pełna lista dla wybranej zależności (A) jest w sekcji 3. Zależności D i C pominąłem w pełnym wyliczeniu, bo nie wygrały klasyfikacji.

## 2. Klasyfikacja i wybór #1

Osie: (a) liczba plików/warstw, (b) koszt wymiany biblioteki dziś, (c) deklaracja wymienialności/izolacji w dokumentach.

| Zależność | (a) pliki / warstwy | (b) koszt wymiany dziś | (c) deklaracja vs kod | Ocena |
|---|---|---|---|---|
| **A PdfPig** | 4 prod. + 9 testowych / 1 warstwa (Import), ale typ w modelu wspólnym dla 3 parserów | Wysoki: zmiana `PdfRectangle` dotyka ~30 miejsc geometrii w 3 parserach (~1150 linii) | Deklaruje izolację (`plan.md:126`, `PdfStatementReader.cs:29`), kod przecieka `PdfRectangle` | **#1** |
| B CsvHelper | 2 / 1 | Średni: dwie konfiguracje, logika wokół `CsvReader` | Research.md:167 stawia CsvHelper obok PdfPig jako „wewnętrzny szczegół parsera” — i jest nim | #2 |
| D EF Core | 18 / wiele endpointów | Bardzo wysoki, ale nikt nie planuje wymiany | Brak deklaracji | #3 (obsługuje plan 02) |
| C Identity | 8 / endpointy | Średni, ale `GetUserId` to jeden punkt | Brak | #4 |
| E, F | 2 / 1 | Niski | Brak | Pomijam |

**Dlaczego PdfPig, mimo że EF Core dotyka więcej plików:** (1) to jedyna zależność, której typ wycieka *z modułu, który miał ją schować* — `PdfStatementReader` istnieje po to, żeby PdfPig znał jeden plik, a publiczny `PdfShape` oddaje jego typ trzem parserom; (2) tylko tu jest jawna intencja izolacji, którą kod łamie; (3) koszt wymiany jest realny: PdfPig 0.x, a każdy nowy bank to kolejny parser kopiujący geometrię na typach biblioteki (obecnie 3 × ok. 10 użyć). EF Core zostaje poza zakresem: to wybór architektoniczny (plan 02 wprowadza port dla `Transaction`), nie przeciek.

**Uczciwe zastrzeżenie.** To najlepszy kandydat, ale przeciek jest wąski: biblioteka ma w repo jedno wejście (`PdfDocument.Open`, `PdfStatementReader.cs:67`), a wyciek to jeden typ geometryczny. Refaktor jest więc tani i niskiego ryzyka; nie jest to „PdfPig rozlany po całym kodzie”.

## 3. Diagnoza: PdfPig

**Co jest zrobione dobrze (do zachowania):** `PdfWord` (`PdfStatementReader.cs:8`) jest już typem domenowym — tekst + środek + linia bazowa, bez typów biblioteki. Cała ekstrakcja słów jest w `ReadPage` (`:91-108`), a wyjątki połyka jeden catch-all (`:45-48`).

**Przeciek 1 — typ biblioteki w modelu publicznym.**
`BE/Import/Pdf/PdfStatementReader.cs:12`:
`public sealed record PdfShape(PdfRectangle Bounds, bool IsFilled, bool IsStroked);`
`PdfRectangle` pochodzi z `UglyToad.PdfPig.Core` (`:3`). Ten sam plik, który obiecuje „one PdfPig access pattern” (`:16-19`), wystawia typ PdfPig przez `PdfPageContent.Shapes` (`:14`).

**Przeciek 2 — trzy parsery znają ten typ i jego semantykę.**
Każdy ma `using UglyToad.PdfPig.Core;` wyłącznie z tego powodu:

- `BE/Import/ErstePdfParser.cs:5`; sygnatury `:117,160,164,192`; członkowie `.Left/.Right/.Top/.Bottom/.Width/.Height` m.in. `:134-135,143,165-167,180,186-187,194-196,207-208`;
- `BE/Import/MBankPdfParser.cs:4`; sygnatury `:125,126`; członkowie `:127,134,136,145,150,163-164`;
- `BE/Import/VeloBankPdfParser.cs:4`; sygnatury `:110,153,176`; członkowie `:127-128,136,160,163,169-170,178-180,190-192`.

**Przeciek 3 — nieudokumentowana konwencja współrzędnych jest zaszyta w parserach.** Oś Y rośnie ku górze strony (dokumentacja PdfPig: „origin is the lower left corner of the page. Therefore a higher Y value means closer to the top”). Parsery zakładają to milcząco: `.OrderByDescending(r => r.Top)` (`ErstePdfParser.cs:135`), `w.CenterY < band.Top && w.CenterY > band.Bottom` (`ErstePdfParser.cs:234`, `VeloBankPdfParser.cs:216`). Gdyby biblioteka miała początek w lewym górnym rogu, wszystkie trzy parsery zepsułyby się *po cichu* (zła kolejność wierszy), nie błędem kompilacji.

**Duplikacja (ta sama rekonstrukcja geometrii w kilku miejscach).** Erste i VeloBank mają niemal identyczny kod na typie biblioteki:

- `FilledRectangles`: `ErstePdfParser.cs:117-118` ≡ `VeloBankPdfParser.cs:110-111` (`page.Shapes.Where(s => s.IsFilled).Select(s => s.Bounds)`);
- filtr komórek nagłówka po `Height`/`Width` i sortowanie po `Top`: `ErstePdfParser.cs:134-135` ≡ `VeloBankPdfParser.cs:127-128`;
- `TryBuildHeaderBlock`: `ErstePdfParser.cs:160-187` ≈ `VeloBankPdfParser.cs:153-170`;
- `FindSeparators`: `ErstePdfParser.cs:192-196` ≈ `VeloBankPdfParser.cs:176-180`;
- `FindBands`: `ErstePdfParser.cs:203-223` ≈ `VeloBankPdfParser.cs:187-207`.

Dodatkowo wiedza o formacie PDF jest zduplikowana poza PdfPig: magiczny prefiks `%PDF-` jest zdefiniowany dwa razy — `BE/Import/Pdf/PdfStatementReader.cs:26` i `BE/Import/StatementFormatSniffer.cs:9`. To nie jest przeciek biblioteki, ale ta sama wiedza w dwóch miejscach; zapisuję jako dług do świadomej decyzji, nie do tej zmiany.

**Przeciek 4 — testy znają bibliotekę po obu stronach.** 9 plików testowych używa PdfPig: `BE/Tests/Import/Parsers/{Erste,MBank,VeloBank}PdfFixtureTests.cs` (`PdfDocument.Open`; `FilledRectangles(Page …)` zwraca `List<PdfRectangle>`, np. `ErstePdfFixtureTests.cs:40`, `VeloBankPdfFixtureTests.cs:38`), `…PdfParserTests.cs` (po jednym użyciu `PdfDocumentBuilder`) i buildery `BE/Tests/Support/{Erste,MBank,VeloBank}PdfBuilder.cs` (`PdfPageBuilder.DrawRectangle/DrawLine/AddText`, np. `ErstePdfBuilder.cs:207-225`, `MBankPdfBuilder.cs:338`). Buildery to generator syntetycznych fixture’ów PDF — **legalnie** używają PdfPig do *pisania* PDF (`context/archive/2026-10-02-pdf-statement-import/plan.md:64`). Kryterium z kroku 6 dotyczy więc kodu produkcyjnego; testy są osobną, uzasadnioną listą (sekcja 6).

**Czego NIE ma (sprawdzone):** typów PdfPig w DTO/kontrakcie wire (`BE/Import/ImportContracts.cs`), w `NormalizedTransaction`, w `IBankStatementParser`, ani po stronie frontendu — `grep UglyToad|PdfPig` w `FE/` zwraca zero. Przeciek nie przekracza katalogu `Import/`.

## 4. Projekt ACL

Zasada: **jedyne miejsce, które zna kształt PdfPig, to adapter**. Reszta zna wartość domenową (`PageBox`) i wąski port.

### 4.1 Value object — `PageBox`

Zastępuje `PdfRectangle` w modelu strony. Namespace `MyFinances.Api.Import.Pdf` (zgodnie z regułą „namespace = ścieżka” katalog `Import/Pdf/`).

```csharp
// BE/Import/Pdf/PageBox.cs — sygnatura, nie implementacja
// Konwencja domenowa (jedyne miejsce, gdzie jest zapisana): oś Y rośnie ku górze strony, Top > Bottom,
// jednostki to punkty PDF. Adapter odpowiada za sprowadzenie do tej konwencji.
public readonly record struct PageBox(double Left, double Right, double Bottom, double Top)
{
    public double Width => Right - Left;
    public double Height => Top - Bottom;
    public double CenterX => (Left + Right) / 2;
    public double CenterY => (Bottom + Top) / 2;
    public bool IsAbove(PageBox other) => Bottom >= other.Top;
    public bool SharesLeftEdgeWith(PageBox other, double tolerance) => Math.Abs(Left - other.Left) <= tolerance;
    public bool ContainsCenterY(double y) => y < Top && y > Bottom;   // zastępuje w.CenterY < band.Top && > band.Bottom
}

public sealed record PdfShape(PageBox Bounds, bool IsFilled, bool IsStroked);
```

Operacje domenowe (`ContainsCenterY`, `SharesLeftEdgeWith`) zbierają porównania geometryczne, które dziś są kopiowane do trzech parserów — to dopiero ten krok zdejmuje duplikację z sekcji 3.

### 4.2 Port (wąski, domenowy)

```csharp
// BE/Import/Pdf/IPdfPageSource.cs
public interface IPdfPageSource
{
    // null = nieczytelny/nie-PDF/za dużo stron/odrzucony przez isRecognised; nigdy nie rzuca.
    List<PdfPageContent>? TryRead(Stream fileStream, int maxPages, bool allPages, Func<PdfPageContent, bool> isRecognised);
}
```

Port zawiera dokładnie to, co dziś robi `PdfStatementReader.TryRead` (`:35-36`); nic z PdfPig w sygnaturze. `JoinCell`, `ParseNumber`, `Rejection`, `NumberPattern` (`:24,113,132,137`) to logika bez PdfPig — zostają w `PdfStatementReader` (zmieniam jego rolę na „pomocniki domenowe PDF”, bez `using UglyToad`).

### 4.3 Adapter

```csharp
// BE/Import/Pdf/PdfPig/PdfPigPageSource.cs — JEDYNY plik z `using UglyToad.PdfPig*`
public sealed class PdfPigPageSource : IPdfPageSource
{
    public List<PdfPageContent>? TryRead(Stream fileStream, int maxPages, bool allPages, Func<PdfPageContent, bool> isRecognised)
    {
        // = dzisiejsze PdfStatementReader.TryRead + Extract + ReadPage (:35-108), bez zmian zachowania:
        //   catch-all => null, przywrócenie pozycji strumienia, kopia bajtów, limit stron, allPages
        // + ToBox(PdfRectangle r) => new PageBox(r.Left, r.Right, r.Bottom, r.Top)
        // + shapes: p.GetBoundingRectangle() jest nullable => pomijane (:103), tak jak dziś
    }
}
```

Rejestracja: `services.AddSingleton<IPdfPageSource, PdfPigPageSource>()` w `BE/DI/ImportServiceCollectionExtensions.cs`. Parsery PDF dostają port przez konstruktor obok istniejącego `maxPages` (`ErstePdfParser.cs:28`, `MBankPdfParser.cs:23`, `VeloBankPdfParser.cs:22`); testy jednostkowe parserów mogą wstrzyknąć fałszywe strony bez generowania PDF.

## 5. Dowód izolacji, before/after, kontrakt biblioteki

**Dowód (lista):** po refaktorze wymiana PdfPig dotyka:

1. `BE/Import/Pdf/PdfPig/PdfPigPageSource.cs` (przepisanie `ReadPage` i `ToBox`),
2. `BE/MyFinances.Api.csproj:28` (`PackageReference`),
3. buildery testowe (osobna, jawna lista w sekcji 6).

Nie dotyka: tabel/migracji (parser zwraca `NormalizedTransaction`, `BE/Import/NormalizedTransaction.cs`), DTO i endpointów (`ImportContracts.cs`, `ImportEndpoints.cs` znają tylko `IBankStatementParser`), frontendu (nie zna PDF), `PageBox` ani logiki parserów banków.

**Before / after — zduplikowane miejsca.**

| Miejsce | Before | After |
|---|---|---|
| Typ kształtu | `PdfShape(PdfRectangle Bounds, …)` (`PdfStatementReader.cs:12`) | `PdfShape(PageBox Bounds, …)` |
| Wejście do biblioteki | statyczne `PdfStatementReader.TryRead` (`:35`) | `IPdfPageSource` + `PdfPigPageSource` (wstrzykiwany) |
| `FilledRectangles` | skopiowane `ErstePdfParser.cs:117-118`, `VeloBankPdfParser.cs:110-111` | pomocnik na `PdfPageContent` (jeden), np. `page.FilledBoxes()` |
| Test „czy środek słowa w paśmie” | `w.CenterY < band.Top && w.CenterY > band.Bottom` (`ErstePdfParser.cs:234`, `VeloBankPdfParser.cs:216`) | `box.ContainsCenterY(w.CenterY)` |
| `using UglyToad.PdfPig.Core` | 3 parsery + reader | tylko adapter |

**Co dostaje warstwa wyżej:** parsery dostają gotowe `PdfPageContent` (`PdfWord` + `PdfShape` z `PageBox`), a endpoint importu dostaje `NormalizedTransaction`/`ParseResult` — żaden surowy obiekt PdfPig nie opuszcza adaptera. UI (frontend) nigdy nie widzi PDF.

**Otwarte pytania zależne od kontraktu biblioteki — rozstrzygnięcie z dokumentacji (Context7 `/uglytoad/pdfpig`):**

| Pytanie | Rozstrzygnięcie z dokumentacji | Gdzie zakodować |
|---|---|---|
| Układ współrzędnych? | Początek w lewym dolnym rogu, wyższy Y = wyżej na stronie; punkt (0,0) to dół strony | Konwencja w komentarzu typu `PageBox` + test charakteryzujący; adapter przeliczy, jeśli biblioteka się zmieni |
| `GetBoundingRectangle()` zwraca `PdfRectangle?` | Typ nullable w `PdfPath` — ścieżka bez geometrii nie ma prostokąta | Adapter (pomijanie, jak `PdfStatementReader.cs:103`) — nie parser |
| Co z PDF zaszyfrowanym/z hasłem? | `PdfDocument.Open` ma `ParsingOptions` z `Password`/`Passwords`; bez hasła otwarcie rzuca | Adapter: zachowanie „nieczytelny = null” (`PdfStatementReader.cs:29-34`) zostaje w ACL; ewentualne hasło byłoby opcją adaptera, nie parsera |
| Czy `IsFilled`/`IsStroked` są wiarygodne? | Flagi `PdfPath` są prawdziwe, ale generator testowy rysuje prostokąty z obwódką (plan `2026-10-02-pdf-import/plan.md:75`) | Selekcja „po `IsFilled` i wymiarach” zostaje w parserach banków (`ErstePdfParser.cs:114`) — to reguła banku, nie biblioteki |
| Czy słowa się dzielą po spacjach? | README PdfPig: tekst może mieć brakujące spacje i niespodziewaną kolejność | `JoinCell` (`:113`) zostaje w pomocnikach domenowych; nie przenosić do adaptera |

Ważne: żadna z tych decyzji nie trafia do endpointów ani DTO — wszystkie żyją w adapterze albo w typie `PageBox`.

## 6. Weryfikacja i plan faz

**Kryterium sukcesu (do uruchomienia przez użytkownika):**

```bash
grep -rln "UglyToad" MyFinances/backend --include=*.cs --exclude-dir=Tests --exclude-dir=obj --exclude-dir=bin
```

Oczekiwany wynik po refaktorze: **dokładnie jeden plik** — `Import/Pdf/PdfPig/PdfPigPageSource.cs`. Dziś: 4 pliki (`PdfStatementReader.cs`, `ErstePdfParser.cs`, `MBankPdfParser.cs`, `VeloBankPdfParser.cs`).

| | Dziś (produkcja) | Po refaktorze |
|---|---|---|
| Znają PdfPig | `Import/Pdf/PdfStatementReader.cs`, `Import/ErstePdfParser.cs`, `Import/MBankPdfParser.cs`, `Import/VeloBankPdfParser.cs` | `Import/Pdf/PdfPig/PdfPigPageSource.cs` |
| Nie znają | pozostałe pliki backendu i cały frontend | wszystkie poza adapterem |

Wyjątek jawny, nie przemilczany: **testy nadal znają PdfPig** (9 plików z sekcji 3, przeciek 4) — służą do *generowania* fixture’ów PDF, a `FixtureTests` sprawdzają ich budowę. Kryterium tam nie obowiązuje; egzekwuje je allowlista w teście architektonicznym (faza 3). `PackageReference` do PdfPig w `.csproj` zostaje (adapter + testy).

**Plan faz** (konwencja projektu: osobna zmiana w `context/changes/`, każda faza odwracalna i commitowana osobno, `Automated` / `Manual` w Progress; zasada kursu „refaktor ≠ zmiana zachowania”):

- **Faza 0 — straż.** Przed ruszeniem czegokolwiek uruchomić istniejące testy parserów i fixture’ów (`dotnet test` z `MyFinances/backend`). Nie ma testów dla geometrii `PageBox` — dodać test charakteryzujący konwencję Y (kolejność wierszy dla fixture’a wielostronicowego `erste-pdf-sample-synthetic-multipage.pdf` i `mbank-pdf-sample-synthetic-multipage.pdf` z `BE/Tests/Fixtures/`). Wynik: zielone, zachowanie zapisane. Odwrócenie: usunięcie nowych testów.
- **Faza 1 — value object.** Dodać `PageBox`, zmienić `PdfShape.Bounds` na `PageBox`, mapowanie `PdfRectangle → PageBox` w `ReadPage`. Parsery przechodzą na `PageBox` (usunięcie trzech `using UglyToad.PdfPig.Core`) i pomocniki geometrii (`ContainsCenterY`, wspólne `FilledBoxes`). Zachowanie bez zmian — testy z fazy 0 zielone bez edycji asercji. Odwrócenie: `git revert` fazy.
- **Faza 2 — port i adapter.** Wydzielić `IPdfPageSource` + `PdfPigPageSource` do `Import/Pdf/PdfPig/`; `PdfStatementReader` traci `TryRead`/`Extract`/`ReadPage` i `using UglyToad.PdfPig*`; wstrzyknięcie portu przez konstruktory parserów PDF; rejestracja w `ImportServiceCollectionExtensions.cs`. Uwaga na rejestrację: parsery są dziś `AddScoped` (`:14-16`) z domyślnym `maxPages` — zachować argument opcjonalny. Wynik: grep z kryterium zwraca jeden plik.
- **Faza 3 — egzekwowanie.** Test architektoniczny (xUnit): skanuje `BE/**/*.cs` poza `Tests/`, `obj/`, `bin/` i oblewa, gdy `UglyToad` występuje poza `Import/Pdf/PdfPig/`. Test w katalogu `BE/Tests/`, w stylu istniejących (AAA, `IAsyncLifetime` nie jest potrzebne — brak zasobów). Opcjonalnie: ten sam wzorzec dla CsvHelper, jeśli zdecydujemy się objąć go kolejnym ACL.
- **Faza 4 — (opcjonalna) dług poboczny.** Jedno źródło prawdy dla `%PDF-` (`StatementFormatSniffer.cs:9` vs `PdfStatementReader.cs:26`); tylko po osobnej decyzji, bo dotyka sniffera.

**Zależności z innymi planami.** Faza 2 nie dotyka `Transaction` ani repozytorium z planu 02 — obie zmiany są niezależne i mogą iść w dowolnej kolejności. Faza 1 po zmianie nazw: jeśli plan 02 doda nowe nazwy do rejestru, `PageBox`/`IPdfPageSource` należy dopisać do tabeli „Nowe nazwy do zarejestrowania” w `02-invariant-aggregate-refactor.md` oraz do `glossary.md` dopiero po implementacji (nazwy są techniczne, nie domenowe — glosariusz domeny ich nie potrzebuje; rejestr kontraktów `docs/reference/contract-surfaces.md` nadal nie istnieje).

## 7. Decyzje i ryzyka

- **Wybór wąski, świadomie.** PdfPig wygrał, bo łamie własną deklarację izolacji; EF Core (więcej plików) i CsvHelper (duplikacja konfiguracji w 2 parserach) zostają jako kandydaci na kolejne ACL-e. Kolejność po PdfPig, ustalona przez właściciela: #2 CsvHelper (duplikacja konfiguracji w 2 parserach, `CsvReader` nie wychodzi poza prywatne metody), #3 EF Core (objęty planem 02), dalej Identity, Recharts, Sonner.
- **Ryzyko faz 1–2:** błąd w zamianie `Top`/`Bottom` w `PageBox` przeszedłby kompilację i zepsuł kolejność wierszy po cichu — stąd test charakteryzujący w fazie 0 i konstruktor z jawnie nazwanymi polami.
- **Czego ten plan nie zmienia:** logiki rozpoznawania banków, integralności sald, formatów CSV, kontraktu `IBankStatementParser`.
