---
title: "MyFinances — niezmiennik #1 i agregat-strażnik (plan refaktoru)"
created: 2026-10-16
type: refactor-plan
repository: "D:\\repos\\michLukaszewicz\\Kurs 10xDev (MyFinances, main bd38988)"
lesson: "10xDevs M4L5 — Modernizacja legacy z DDD, krok 2"
---

# Niezmiennik #1: tożsamość zaimportowanej transakcji z momentu importu

Ścieżki są względne do korzenia repozytorium. `BE` = `MyFinances/backend`, `FE` = `MyFinances/frontend/app`, `ctx` = `context`. To jest PLAN: nie zmieniałem kodu produkcyjnego ani testów.

## Wynik w skrócie

| | |
|---|---|
| **Wybrany niezmiennik** | Zaimportowana transakcja zachowuje hash z importu po edycji pól (to jej tożsamość dla wykrywania duplikatów). Hash ręcznego wpisu zawsze odpowiada wpisanym wartościom i jest przeliczany przy edycji (decyzja Q-1). |
| **Agregat-strażnik** | `Transaction` jako root, z prywatnym setterem `Hash` i dwiema fabrykami (`FromImport`, `EnteredManually`). |
| **Dlaczego #1** | Najbardziej rdzeniowy (Guardrail PRD: duplikaty nigdy po cichu podwójnie), a dziś kod mu **przeczy** przy zwykłej edycji, bez żadnego błędu. |
| **Czego to nie jest** | Zmiana zachowania. Zachowanie zmienia dopiero osobna Faza 2, bo twarda reguła kursu mówi, że refaktor nie zmienia obserwowalnego zachowania. |

## Krok 0 — Kontekst

**Dokumenty.** `ctx/foundation/prd.md` (v4), `roadmap.md` (v2), `lessons.md`, `test-plan.md`, `ctx/domain/01-domain-distillation.md`. Brak README z regułami domeny, brak `docs/reference/contract-surfaces.md` (rejestr kontraktów wspomniany w `.claude/CLAUDE.md` nie istnieje w repo; nowe nazwy podaję na końcu).

**Decyzje właściciela z 2026-10-16, które są wejściem do tego planu** (zapisane w PRD v4):
- FR-010 (`ctx/foundation/prd.md:94-95`): ręczny wpis jest haszowany z tego, co wpisał użytkownik, i nie jest dopasowywany do importu; zaimportowany wiersz **zachowuje hash po edycji**.
- FR-009 (`prd.md:80-82`): po usunięciu jednej strony pary druga zostaje przelewem wewnętrznym.
- FR-019 (`prd.md:83`): rodzaj kategorii musi zgadzać się ze znakiem kwoty, egzekwowane na serwerze.
- FR-020 (`prd.md:90`): unikalność rachunku po banku i numerze.

**Stack i warstwy.** ASP.NET Core minimal API (net10.0) + EF Core/Npgsql; React Router SPA.
- **Endpointy** (`BE/*/*Endpoints.cs`): prawie cała logika, w tym wszystkie zapisy `Transaction`.
- **Pure helper:** `BE/Import/DedupHash.cs:11-23` (SHA-256 z `userId|date|amount|description|accountId`).
- **Usługa:** `BE/Categorization/TransferDetectionService.cs`.
- **Encja anemiczna:** `BE/Transactions/Transaction.cs:8-43`, same publiczne get/set.
- **Persystencja:** `BE/AppDbContext.cs:23-25` (indeks nieunikalny `(UserId, Hash)`), snapshot `BE/Migrations/AppDbContextModelSnapshot.cs:274,298`.
- **Klient:** nie liczy hasha i nie zna go; w tej regule nie uczestniczy.

## Krok 1 — Niezmienniki

Źródła: dokumenty (PRD v4, plan-briefy w archiwum) i kod. Numeracja N-xx jest lokalna dla tego planu (nie myli się z I-xx z destylacji).

| ID | Reguła, która musi być zawsze prawdziwa | Źródło | Gdzie w kodzie |
|---|---|---|---|
| N-01 | Hash zaimportowanej transakcji jest nadawany przy imporcie i nie zmienia się przy edycji; hash ręcznego wpisu odpowiada aktualnie wpisanym wartościom | `ctx/foundation/prd.md:41,94-95` | `BE/Transactions/TransactionEndpoints.cs:222` (przeczy) |
| N-02 | Kategoria przypisana do transakcji ma rodzaj zgodny ze znakiem kwoty | `prd.md:83` | `FE/lib/categories.ts:12-20` (tylko klient) |
| N-03 | Para przelewu wewnętrznego jest wykluczona z wykresów; ręczna decyzja nie jest nadpisywana; flaga zostaje po usunięciu partnera | `prd.md:80-82` | `BE/Categorization/TransferDetectionService.cs:19-22,45-49`, `BE/Categorization/CategorizationEndpoints.cs:201-206`, `BE/Dashboard/DashboardEndpoints.cs:66-71` |
| N-04 | Każdy wykryty duplikat przechodzi przez decyzję użytkownika; commit odtwarza tylko to, co zostało sparsowane | `prd.md:73` (FR-004) | `BE/Import/ImportEndpoints.cs:142-144` (komentarz) vs `:151-199` |
| N-05 | Rachunek jest unikalny po banku i numerze | `prd.md:90` | `BE/AppDbContext.cs:46-48` (po etykiecie, nie po banku) |
| N-06 | Każde zapytanie i zapis są ograniczone do właściciela danych | `prd.md` NFR, `ctx/foundation/test-plan.md` ryzyko 3 | filtr `UserId ==` powielony w endpointach; pilnowany testami `BE/Tests/Ownership/*` |
| N-07 | Wyciąg PDF z niesumującymi się saldami jest odrzucony | `prd.md:69` (FR-018) | `BE/Import/ImportEndpoints.cs:73-82`, parsery PDF |
| N-08 | Usunięcie używanej kategorii lub rachunku wymaga zgody | archiwum `ctx/archive/2026-10-09-custom-categories-and-uncategorized-indicator/change.md`, `prd.md:90` | `BE/Categorization/CategorizationEndpoints.cs:98-127`, `BE/Transactions/AccountEndpoints.cs:110-145` |
| N-09 | Nowy użytkownik zawsze ma domyślne kategorie | archiwum `2026-10-09-custom-categories…/change.md` | `BE/Auth/AuthEndpoints.cs:33,39-40` (dwa osobne zapisy) |
| N-10 | Ręczny wpis zawsze ma kategorię, import zaczyna bez kategorii | `ctx/archive/2026-09-25-manual-transaction-entry/plan-brief.md:37` | `BE/Transactions/TransactionContracts.cs:16`, `BE/Import/ImportEndpoints.cs:189-199` |

## Krok 2 — Klasyfikacja i wybór #1

Osie: (a) rdzeniowość dla produktu, (b) rozsmarowanie (pliki i warstwy), (c) egzekwowanie: **egzekwowany** / **deklarowany** (komentarz lub UI) / **naruszalny** / **przeczy**.

| ID | (a) rdzeniowość | (b) rozsmarowanie | (c) egzekwowanie |
|---|---|---|---|
| **N-01** | **Wysoka.** Guardrail Primary (`prd.md:41`): bez niego north star (`roadmap.md` S-01) traci sens, bo wykres liczy dane dwukrotnie | 3 pliki produkcyjne: `Import/DedupHash.cs`, `Import/ImportEndpoints.cs` (5 wywołań), `Transactions/TransactionEndpoints.cs` (3 miejsca); 1 test przypinający stare zachowanie | **Przeczy.** Kod świadomie przelicza hash przy edycji i test to utrwala |
| N-02 | Wysoka: donut przychodów i wydatków, trend | klient (2 trasy + helper), 3 endpointy zapisu bez kontroli, 2 endpointy agregacji liczą po znaku | **Deklarowany** (tylko filtr listy w UI); serwer pozwala |
| N-03 | Wysoka | detektor, endpoint kategoryzacji, 5 endpointów GET z zapisem, dashboardy | Częściowo egzekwowany; usunięcie partnera rozstrzygnięte; edycja po sparowaniu otwarta |
| N-04 | Średnia-wysoka | endpoint parse + commit + klient | **Deklarowany** w komentarzu; w praktyce przelicza tylko hash |
| N-05 | Niska | DB + 2 endpointy | Naruszalny (indeks po złej kolumnie, wyścig daje 500) |
| N-06 | Wysoka | każdy endpoint | Egzekwowany **testami**, nie strukturą (filtr kopiowany, helper `OwnedBy` tylko dla kategorii) |
| N-07 | Wysoka dla PDF | 3 parsery + endpoint | Egzekwowany dla PDF; CSV bez kontroli (decyzja odłożona) |
| N-08 | Średnia | 2 endpointy | Egzekwowany (409 + FK `Restrict`) |
| N-09 | Niska | 1 endpoint | Naruszalny (brak transakcji) |
| N-10 | Niska | 2 endpointy | Egzekwowany typem (`Guid`) |

**Wybór: N-01.** Jest jednocześnie (1) najbardziej rdzeniowy wśród tych, które są realnie złamane, bo dotyka głównego Guardrail produktu, i (2) jedynym, któremu kod nie tylko nie pomaga, ale **przeczy**: wcześniejsza decyzja projektowa („hash przeliczany przy edycji”, `ctx/archive/2026-09-25-manual-transaction-entry/plan-brief.md:39`, `plan.md:96,138,162`) jest dziś odwrócona decyzją właściciela. Łamie się przy zwykłym działaniu, nie przy wyścigu ani złośliwym kliencie: formularz edycji na dashboardzie pozwala zmienić datę, opis i kwotę dowolnej transakcji (`FE/routes/home.tsx:385-392`). Efekt jest cichy: po edycji ponowny import tego samego wyciągu nie oznacza wiersza jako duplikatu i transakcja liczy się podwójnie.

**Dlaczego nie N-02 (drugi w kolejce).** Dotyka tego samego agregatu i został już rozstrzygnięty, ale psuje klasyfikację (która kategoria na jakim wykresie), a nie liczbę transakcji, i ma ograniczony zasięg: wymaga pomyłki przy wyborze kategorii, którą UI utrudnia. Wchodzi jako Faza 3 tego samego agregatu. N-03 straciło pozycję #1 po decyzji o usuwaniu partnera (patrz `01-domain-distillation.md`, ocena ponownie).

## Krok 3 — Diagnoza N-01

Gdzie reguła żyje dziś (wszystkie cytaty sprawdzone w kodzie):

**Warstwa domeny / encja**
- `BE/Transactions/Transaction.cs:24`: `public required string Hash { get; set; }`, publiczny setter, każdy kod może przypisać dowolny hash.
- `BE/Transactions/Transaction.cs:5-7`: komentarz mówi tylko o tym, że hash nie jest unikalny; o niezmienności nic.

**Warstwa API (każde miejsce liczy hash samo)**
- Tworzenie ręczne: `BE/Transactions/TransactionEndpoints.cs:116` (liczy), `:137-148` (inicjalizator obiektu z `Hash = hash`).
- **Edycja: `BE/Transactions/TransactionEndpoints.cs:193` (liczy z nowych wartości), `:214-216` (komentarz uzasadnia przeliczenie), `:222` (`transaction.Hash = hash`). To jest miejsce, w którym reguła jest łamana.** Edycja obejmuje też transakcje zaimportowane, bo endpoint ładuje po `Id` i `UserId` bez rozróżnienia (`:175`).
- Import, parse: `BE/Import/ImportEndpoints.cs:85` i `:100` (dwa razy to samo obliczenie).
- Import, commit: `BE/Import/ImportEndpoints.cs:153,158` (liczy dla `Keep` i `Skip`), `:189-199` (inicjalizator z `Hash = hash`).
- Razem 6 wywołań `DedupHash.ComputeHash` (4 w imporcie, 2 w transakcjach) i 3 przypisania `Hash` (`ImportEndpoints.cs:197`, `TransactionEndpoints.cs:145`, `:222`) w 2 plikach endpointów.

**Warstwa persystencji**
- `BE/AppDbContext.cs:23-25`: indeks `(UserId, Hash)` nieunikalny, zamierzony; nic nie chroni niezmienności.

**Test, który utrwala złe zachowanie**
- `BE/Tests/Transactions/TransactionEndpointsTests.cs:789` `Put_RecomputesHash_LeavesImportBatchIdUnchanged`, asercja `:827-828` (hash równy hashowi z nowych pól). Po decyzji właściciela ten test opisuje zachowanie, którego nie chcemy.

**Klient.** Nie ma w tym udziału: nie liczy hasha i nie zna go. Klient nie jest tu jedynym strażnikiem, ale też nie pomaga: formularz edycji (`FE/routes/home.tsx:385-392`) wysyła `PUT` z pełnym zestawem pól także dla wierszy z importu.

**Czego nie egzekwuje żadna warstwa.** Nic nie odróżnia transakcji z importu (`ImportBatchId != null`, `BE/Transactions/Transaction.cs:27`) od ręcznej w momencie edycji. Błąd nie jest połykany: jest wykonywany jako zamierzone zachowanie.

**Scenariusz awarii.** Import wyciągu mBank → wiersz „ABC” 50,00. Użytkownik poprawia opis w dashboardzie na „Zakupy”. `PUT` przelicza hash z nowego opisu. Ponowny import tego samego wyciągu (np. nakładający się miesiąc) liczy hash „ABC” i nie znajduje go w bazie, więc wiersz nie jest oznaczony jako duplikat. Domyślna decyzja `Keep` wstawia drugą kopię, a wykresy liczą wydatek dwa razy.

## Krok 4 — Projekt agregatu-strażnika

**Root: `Transaction`.** Jedyny typ, który może ustawić `Hash`. Inwariant: `Hash` jest funkcją (`userId`, data, kwota, opis, rachunek). Dla transakcji z importu (`ImportBatchId != null`) liczony raz, w chwili importu, i potem stały. Dla ręcznego wpisu (`ImportBatchId == null`) liczony z aktualnych wartości i przeliczany przy każdej edycji.

Sygnatury (szkic, nie implementacja; w kodzie produkcyjnym trzymać styl sąsiednich plików):

```csharp
public class Transaction
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid AccountId { get; private set; }
    public DateOnly Date { get; private set; }
    public string Description { get; private set; }
    public decimal Amount { get; private set; }
    public string Hash { get; private set; }
    public Guid? ImportBatchId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public bool IsInternalTransfer { get; private set; }
    public bool TransferFlagManuallySet { get; private set; }

    private Transaction() { }

    public static string IdentityFor(Guid userId, DateOnly date, decimal amount, string description, Guid accountId);
    public static Transaction FromImport(Guid userId, Guid accountId, Guid importBatchId, DateOnly date, decimal amount, string description);
    public static Transaction EnteredManually(Guid userId, Guid accountId, Guid categoryId, DateOnly date, decimal amount, string description);
    public void CorrectDetails(DateOnly date, decimal amount, string description, Guid accountId);
    public void AssignCategory(Category category);
}
```

**Pseudokod i warunki wstępne.**
- `IdentityFor(...)`: jedyne miejsce, które woła `DedupHash.ComputeHash`. Używają go parse importu (kandydat na duplikat), POST/PUT (kontrola duplikatu) i fabryki. Dzięki temu `grep DedupHash.ComputeHash` ma jeden wynik poza testami.
- `FromImport(...)`: wymaga `userId != Guid.Empty`, `accountId != Guid.Empty`, `importBatchId != Guid.Empty`, niepustego opisu. Naruszenie rzuca `TransactionIdentityException` (nazwany błąd domenowy). Ustawia `Hash = IdentityFor(...)`, `CategoryId = null`.
- `EnteredManually(...)`: te same warunki dla `userId`, `accountId`, opisu, plus `categoryId != Guid.Empty`; `ImportBatchId = null`, `Hash = IdentityFor(...)` z wartości, które wpisał użytkownik (decyzja FR-010).
- `CorrectDetails(...)`: zmienia datę, kwotę, opis i rachunek. Dla transakcji z importu **nie dotyka `Hash`** (po Fazie 2); dla ręcznego wpisu przelicza go przez `IdentityFor` z nowych wartości. Ma te same warunki co fabryki dla rachunku i opisu. W Fazie 1 przeliczanie zostaje dla obu rodzajów, żeby zachowanie się nie zmieniło.
- `AssignCategory(Category category)`: dodane w Fazie 3: rzuca `CategoryKindMismatchException`, gdy `category.Kind` nie pasuje do znaku `Amount`.

**Repozytorium zamiast rozsianych zapytań.**

```csharp
public interface ITransactionRepository
{
    Task<Transaction?> GetOwnedAsync(Guid userId, Guid id);
    Task<ExistingTransactionDto?> FindDuplicateAsync(Guid userId, string candidateHash, Guid? exceptId);
    void Add(Transaction transaction);
    void AddRange(IEnumerable<Transaction> transactions);
    void Remove(Transaction transaction);
}
```

Implementacja owija `AppDbContext`. Zapytania z `UserId == userId` żyją tylko tu, więc N-06 zyskuje jedno miejsce zamiast kopii (efekt uboczny, poza zakresem).

**Atomiczność.** Sprawdzenie duplikatu i zapis muszą iść w jednej transakcji `Serializable` (dziś `BE/Transactions/TransactionEndpoints.cs:121,198`, `BeginTransactionIfSupportedAsync` z `catch` połykającym `InvalidOperationException`, `:266-276`). Plan: jeden przypadek użycia `SaveTransactionAsync(...)` otwiera transakcję, woła `FindDuplicateAsync`, wywołuje metodę agregatu, robi `SaveChangesAsync` i `CommitAsync`. Commit importu zostaje w jednym `SaveChangesAsync` (partia i jej transakcje razem, `BE/Import/ImportEndpoints.cs:202`), tylko tworzy transakcje przez `FromImport`.

**Cienkie API.** Endpoint: sparsuj `TransactionWriteRequest` → sprawdź własność rachunku i kategorii → `repository.GetOwnedAsync` → polityka duplikatu → metoda agregatu → zapis → mapowanie na DTO. Błędy domenowe mapuje jeden `IEndpointFilter` (`TransactionIdentityException` → 400 `Problem`, `CategoryKindMismatchException` → 400 `Problem` z komunikatem „Category kind does not match the amount sign.”). Egzekucja żadnej reguły nie przenosi się z klienta, bo w N-01 klient nie uczestniczy; w Fazie 3 (N-02) przenosi się z `FE/lib/categories.ts:12-20` na serwer, a klient zostaje tylko jako ułatwienie wyboru.

## Krok 5 — Before / after, plan, testy

### Before / after

| Miejsce | Dziś | Po refaktorze |
|---|---|---|
| `Transaction.cs:24` | publiczny setter `Hash` | `private set`, ustawiany tylko w fabrykach |
| `TransactionEndpoints.cs:116,137-148` (POST) | endpoint liczy hash i ręcznie wypełnia obiekt | `Transaction.EnteredManually(...)` |
| `TransactionEndpoints.cs:193` (PUT, kontrola duplikatu) | liczy hash z nowych pól | `Transaction.IdentityFor(...)` jako kandydat; kontrola duplikatu bez zmian |
| `TransactionEndpoints.cs:214-222` (PUT, zapis) | przypisuje nowy `Hash` | `transaction.CorrectDetails(...)`, hash nietknięty (po Fazie 2) |
| `ImportEndpoints.cs:85,100` (parse) | dwa obliczenia | `Transaction.IdentityFor(...)` |
| `ImportEndpoints.cs:153,158,189-199` (commit) | trzy obliczenia i inicjalizator | `Transaction.IdentityFor` dla `Skip`, `Transaction.FromImport` dla `Keep` |
| `TransactionEndpoints.cs:261-277` | `catch` połyka brak transakcji | bez zmian w tej zmianie; do osobnej oceny |
| `Tests/.../TransactionEndpointsTests.cs:789-829` | asercja: hash przeliczony | odwrócona w Fazie 2: hash niezmieniony |
| Klient `FE/routes/home.tsx:385-392` | wysyła pełny `PUT` | bez zmian |

### Plan faz (każda do cofnięcia osobno; zgodnie z regułami z `CLAUDE.md` Legacy: guard first, brak zmiany zachowania w fazie refaktoru)

| Faza | Zakres | Test-first? | Weryfikacja |
|---|---|---|---|
| **0 — Guard** | Przypiąć dzisiejsze zachowanie: istniejący test `:789` oznaczyć jako charakteryzacyjny; dodać test integracyjny „edytuj zaimportowany wiersz, zaimportuj ten sam plik ponownie → wiersz NIE jest duplikatem” w konwencji `..._KnownLimitation` (`ctx/foundation/test-plan.md:127`). Zero zmian w kodzie produkcyjnym | tak (to są testy) | auto: `dotnet test --filter "FullyQualifiedName~Transaction|FullyQualifiedName~Import"` zielone |
| **1 — Wydzielenie agregatu (zachowanie bez zmian)** | `IdentityFor`, fabryki, prywatne settery (EF zapisuje przez konstruktor bez parametrów), `ITransactionRepository`; endpointy POST/PUT/commit idą przez agregat. `CorrectDetails` nadal przelicza hash | testy domenowe fabryk first, resztą istniejące testy jako siatka | auto: wszystkie testy backendu bez zmian; kryterium z poniższej listy punkt 1 i 2 |
| **2 — Zmiana zachowania (osobny change-id)** | Usunąć przeliczanie hasha z `CorrectDetails` tylko dla transakcji z importu (ręczne wpisy dalej przeliczają); odwrócić test `:789` i test z Fazy 0 (najpierw czerwone, potem zielone) | tak | auto: odwrócone testy zielone; ręcznie: import → edycja opisu → ponowny import pokazuje wiersz jako duplikat |
| **3 — N-02 w tym samym agregacie** | `AssignCategory` rzuca `CategoryKindMismatchException`; POST, PUT i `PUT /categorization/transactions/{id}` wołają ją | tak | auto: przypadki z listy niżej; ręcznie: wybór niezgodnej kategorii przez API zwraca 400 |
| **4 — opcjonalnie: N-03** | `MarkAsInternalTransfer` / `UnmarkInternalTransfer` w agregacie; detektor przestaje przypisywać flagi bezpośrednio; decyzja o edycji po sparowaniu | tak | osobny plan |

### Przypadki testowe N-01 (xUnit, konwencja projektu: `// Arrange`, `// Act`, `// Assert`, `IAsyncLifetime`, `AuthApiFactory`, `ImportTestHelpers`)

**Legalne**
1. `EnteredManually` ustawia `Hash` równy `IdentityFor` z wpisanych wartości (istniejący test `:639`).
2. `FromImport` ustawia ten sam `Hash`, co parse dla tego samego wiersza i rachunku.
3. `CorrectDetails` na transakcji z importu zmienia datę, kwotę, opis i rachunek, a `Hash` zostaje (Faza 2).
3a. `CorrectDetails` na ręcznym wpisie przelicza `Hash` z nowych wartości (zachowanie dzisiejsze, test `:789` przepisany na wariant ręczny).
4. `PUT` na zaimportowanym wierszu zostawia `ImportBatchId` (istniejący `:789`, bez asercji o hashu) i `Hash` z importu.
5. Import → edycja opisu → ponowny parse tego samego pliku zwraca `IsDuplicate == true` dla edytowanego wiersza (przez `ImportTestHelpers`, oczekiwane wartości jako literały, nie odczytane z parsera).
6. `PUT` bez `Force` przeciw hashowi innego wiersza zwraca 409 (istniejący `:832`, bez zmian).
7. `Skip` przy commicie liczy duplikat po hashu z importu, także po edycji wiersza.

**Nielegalne**
8. `FromImport` z `Guid.Empty` jako rachunkiem lub użytkownikiem rzuca `TransactionIdentityException`.
9. `EnteredManually` z pustym opisem rzuca `TransactionIdentityException`.
10. Test architektury: `typeof(Transaction).GetProperty("Hash")!.SetMethod!.IsPublic` jest `false`.
11. Faza 3: `AssignCategory` z kategorią Expense dla dodatniej kwoty rzuca `CategoryKindMismatchException`; endpoint zwraca 400, a stan w bazie jest bez zmian.

### Kryteria „gotowe, gdy…” (sprawdzalne)

1. `grep -rn "DedupHash.ComputeHash" MyFinances/backend --include=*.cs` poza `Tests/` zwraca tylko `Transactions/Transaction.cs`.
2. `grep -rn "\.Hash = \|Hash = " MyFinances/backend --include=*.cs` poza `Tests/`, `obj/`, `Migrations/` zwraca tylko `Transactions/Transaction.cs`.
3. Test z punktu 5 (import → edycja → ponowny import) przechodzi.
4. Test architektury z punktu 10 przechodzi.

### Decyzje przed Fazą 2 (rozstrzygnięte 2026-10-16)

| ID | Pytanie | Moja rekomendacja |
|---|---|---|
| Q-1 | ~~Czy ręcznie wpisana transakcja po edycji też zachowuje hash z chwili wpisania?~~ | **Rozstrzygnięte 2026-10-16: nie.** Ręczny wpis po edycji przelicza hash z nowych wartości; reguła niezmienności dotyczy tylko transakcji z importu. |
| Q-2 | Czy `PUT` ma dalej ostrzegać (409 + „Save anyway”), gdy nowe wartości kolidują z innym wierszem? | **Rozstrzygnięte 2026-10-16: tak.** Tak, bez zmian: kontrola idzie po kandydacie z `IdentityFor`, ale zapis go nie nadpisuje |
| Q-3 | Co z wierszami edytowanymi **przed** zmianą? Ich hash już odpowiada wartościom po edycji, a wartości z importu nie są zapisane. | **Rozstrzygnięte 2026-10-16: ok.** Zostawić; backfill niemożliwy, a skutek (brak wykrycia duplikatu dla dawno edytowanych wierszy) jest ograniczony |

### Poza zakresem tego planu (świadomie)

N-03 (przelew wewnętrzny, edycja po sparowaniu, zapis przy GET), N-04 (zaufanie klientowi przy commicie importu), N-05 (indeks po banku i numerze), N-06 (helper własności dla wszystkich zapytań), N-07 CSV (kontrola sald), N-09 (atomowa rejestracja), brak walidacji pustego opisu i zerowej kwoty w endpointach ręcznych. Każde z nich dostanie własny change-id.

### Nowe nazwy do zarejestrowania

Rejestru kontraktów w repo jeszcze nie ma: plik `docs/reference/contract-surfaces.md` należy do skilli `/10x-contract` i `/10x-impl-review` (`.claude/skills/10x-init/SKILL.md:95`) i tworzy się dopiero przy pierwszym użyciu. Nazwy poniżej jeszcze nie istnieją w kodzie, więc rejestrujemy je po Fazie 1 (kiedy trafią do `main`), a nie wcześniej. Kolumna „Faza” mówi, kiedy nazwa powstaje.

| Nazwa | Rodzaj | Co chroni lub znaczy | Gdzie powstaje | Faza |
|---|---|---|---|---|
| `Transaction` (root agregatu) | klasa domenowa | jedyny strażnik tożsamości transakcji; prywatne settery | `BE/Transactions/Transaction.cs` | 1 |
| `Transaction.IdentityFor` | metoda statyczna | jedyne miejsce wołające `DedupHash.ComputeHash`; hash kandydata i hash zapisywany | `Transaction.cs` | 1 |
| `Transaction.FromImport` | fabryka | tworzy transakcję z importu z hashem z importu, bez kategorii | `Transaction.cs` | 1 |
| `Transaction.EnteredManually` | fabryka | tworzy ręczny wpis z kategorią i hashem z wpisanych wartości | `Transaction.cs` | 1 |
| `Transaction.CorrectDetails` | metoda | zmienia datę, kwotę, opis, rachunek; hash zostaje dla importu, przelicza się dla ręcznego wpisu | `Transaction.cs` | 1 (zachowanie bez zmian), 2 (hash importu stały) |
| `Transaction.AssignCategory` | metoda | odrzuca rodzaj kategorii niezgodny ze znakiem kwoty | `Transaction.cs` | 3 |
| `ITransactionRepository` | interfejs | jedyne miejsce zapytań o transakcje właściciela i kolizje hasha | `BE/Transactions/ITransactionRepository.cs` | 1 |
| `TransactionIdentityException` | błąd domenowy | brak użytkownika, rachunku, partii lub opisu przy tworzeniu; mapowany na 400 | `BE/Transactions/` | 1 |
| `CategoryKindMismatchException` | błąd domenowy | kategoria o rodzaju niezgodnym ze znakiem kwoty; mapowany na 400 | `BE/Categorization/` | 3 |
| Filtr mapujący błędy domenowe | `IEndpointFilter` | jedno miejsce zamiany błędów domenowych na `ProblemDetails` | `BE/Transactions/` | 1 |

Istniejące nazwy, które **zostają bez zmian i nadal są nośne**: kolumna `Transaction.Hash`, indeks `(UserId, Hash)` (nieunikalny, zamierzony), `DedupHash.ComputeHash`, `ImportBatch.SourceFormat`, odpowiedź 409 `DuplicateTransactionResponse` oraz flaga `Force` w `TransactionWriteRequest`. Migracja nie jest potrzebna.

Terminy domenowe z tej zmiany są w glosariuszu (`context/domain/glossary.md`): „Transakcja z importu”, „Ręczny wpis” i „Tożsamość transakcji”.
