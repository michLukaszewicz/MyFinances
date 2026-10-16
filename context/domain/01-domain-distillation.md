---
title: "MyFinances — destylacja domeny (ubiquitous language, subdomeny, niezmienniki, rozjazdy model vs kod)"
created: 2026-10-16
type: domain-distillation
repository: "D:\\repos\\michLukaszewicz\\Kurs 10xDev (MyFinances, commit d9a599e)"
lesson: "10xDevs M4L5 — Modernizacja legacy z DDD"
---

# MyFinances — destylacja domeny

Wszystkie ścieżki są względne do korzenia repozytorium `Kurs 10xDev`. Skróty: `BE` = `MyFinances/backend`, `FE` = `MyFinances/frontend/app`, `ctx` = `context`.

> **Aktualizacja 2026-10-16.** Po tej analizie zaktualizowano `prd.md` do v4 i `roadmap.md` do v2 (PR #51) oraz zarchiwizowano trzy zakończone zmiany. Rozjazdy po stronie dokumentów (D-03, D-06 do D-10) oznaczono jako zamknięte, a D-11 przeniesiono do Open Questions w PRD. **Numery linii w `prd.md` i `roadmap.md` w tym raporcie dotyczą wersji sprzed aktualizacji** (`git show 2f5a76b:context/foundation/prd.md`); numery linii kodu są aktualne. Rozjazdy po stronie kodu (D-01, D-02, D-04, D-05, D-12 do D-14) pozostają otwarte.

**Metoda.** Trzy niezależne przebiegi: (1) tylko dokumenty (`ctx/foundation/*`, `change.md` i plan-brief w `ctx/archive/*` i `ctx/changes/*`), (2) tylko backend, (3) tylko frontend. Przebiegi 2 i 3 nie widziały PRD ani żadnego `.md`, więc słownik z kodu nie jest potwierdzeniem słownika z PRD. Cytaty nośne (te, na których stoją niezmienniki i rozjazdy) sprawdziłem ręcznie w plikach.

## Krok 0 — Kontekst projektu

- **Dokumenty źródłowe:** `ctx/foundation/prd.md` (v3, status `draft`, `context_type: greenfield`), `shape-notes.md`, `roadmap.md` (milestone M-1 zamknięty 2026-10-02), `tech-stack.md`, `tech-stack-frontend.md`, `lessons.md`, `test-plan.md`. Historia zmian: 24 zarchiwizowane zmiany w `ctx/archive/` i 4 otwarte w `ctx/changes/`.
- **Ograniczenie:** PRD nie był aktualizowany po decyzjach z 2026-10-02 i 2026-10-09. Późniejsze decyzje żyją tylko w roadmapie i w `change.md` (patrz rozjazdy oznaczone „dokument”).
- **Stack:** ASP.NET Core minimal API (net10.0) + EF Core (Npgsql) w `BE`; React Router SPA (TS, Vite, Tailwind) w `FE`.
- **Gdzie żyje logika biznesowa:**
  - **Endpointy** (statyczne klasy minimal API) mają prawie całą logikę: walidacje, własność danych, decyzje dedup, kaskady usuwania, ostrzeżenia, agregacje dashboardu. `BE/Import/ImportEndpoints.cs`, `BE/Transactions/*Endpoints.cs`, `BE/Categorization/CategorizationEndpoints.cs`, `BE/Dashboard/*Endpoints.cs`.
  - **Jedna usługa domenowa:** `TransferDetectionService` (`BE/Categorization/TransferDetectionService.cs`).
  - **Czyste helpery z zachowaniem:** `DedupHash`, `CategoryDeviation`, `PeriodRange`, `CurrentMonthRange`, `PolishBanks`, `DefaultCategories`.
  - **Parsery wyciągów** z bogatą logiką (rozpoznanie banku, kontrola integralności): `BE/Import/*Parser.cs`, wspólny `BE/Import/Pdf/PdfStatementReader.cs`.
  - **Encje są w pełni anemiczne:** `Account`, `Transaction`, `ImportBatch`, `Category`, `AppUser` mają tylko publiczne get/set; stan zmieniają endpointy przypisując właściwości (np. `BE/Transactions/TransactionEndpoints.cs:217-222`, `BE/Categorization/CategorizationEndpoints.cs:199-206`).
  - **Klient** ma część reguł, które serwer pomija (dobór kategorii do znaku kwoty, patrz I-03).

## Krok 1 — Ubiquitous Language

### 1.1 Słownik pojęć

| # | Pojęcie (dokument) | Definicja | Źródło w dokumencie | Gdzie żyje w kodzie |
|---|---|---|---|---|
| T-01 | Użytkownik (User) | Jedyna osoba, właściciel wszystkich danych | `ctx/foundation/prd.md:110` | `BE/AppUser.cs:5-8` (`AppUser`); filtr `UserId` powielony w każdym zapytaniu, helper tylko dla kategorii `BE/Categorization/CategoryQueries.cs:5-6` |
| T-02 | Rachunek bankowy (bank account) | Własny rachunek użytkownika: bank + numer | `ctx/foundation/roadmap.md:51` | `BE/Transactions/Account.cs:6-20` (`Account`) |
| T-03 | Nazwa rachunku vs bank | `BankName` to wolna etykieta, `Bank` to wartość z listy polskich banków + „Other” | `ctx/archive/2026-10-02-account-bank-selection/change.md:12-14` | `BE/Transactions/Account.cs:12-17`; `BE/Transactions/PolishBanks.cs:5-54` |
| T-04 | Wyciąg (statement / export) | Plik z banku, CSV lub PDF | `ctx/foundation/prd.md:66` | `BE/Import/StatementFormat.cs:8-13` (`StatementFormat {Csv, Pdf}`), `BE/Import/StatementFormatSniffer.cs:7-39` |
| T-05 | Import | Upload → parse → przegląd duplikatów → commit | `ctx/archive/2026-09-25-mbank-import-with-dedup/plan-brief.md:52` | `BE/Import/ImportEndpoints.cs:18` (`/parse`), `:134` (`/commit`) |
| T-06 | Partia importu (import batch) | Trwały zapis jednego importu, z formatem źródła | `ctx/archive/2026-09-25-mbank-import-with-dedup/plan-brief.md:22` | `BE/Transactions/ImportBatch.cs:6-26` |
| T-07 | Parser banku | Jeden parser na bank i format, wspólny interfejs | `ctx/foundation/shape-notes.md:151` | `BE/Import/IBankStatementParser.cs:14-26`; 5 implementacji rejestrowanych w `BE/DI/ImportServiceCollectionExtensions.cs:12-16` |
| T-08 | Znormalizowana transakcja | Wiersz niezależny od banku: data, opis, kwota | `ctx/foundation/shape-notes.md:151` | `BE/Import/NormalizedTransaction.cs:5` |
| T-09 | Transakcja | Datowana kwota ze znakiem i opisem, przypięta do rachunku; importowana albo ręczna | `ctx/foundation/prd.md:82` | `BE/Transactions/Transaction.cs:8-43`; ręczna = `ImportBatchId == null` (`:26-27`) |
| T-10 | Znak kwoty | Ujemna = wydatek, dodatnia = przychód | `ctx/archive/2026-09-28-category-spend-donut-chart/plan-brief.md:28` | `BE/Dashboard/DashboardEndpoints.cs:69`; `FE/lib/categories.ts:18` |
| T-11 | Duplikat / hash dedup | Kolizja hasha z istniejącą transakcją, do decyzji użytkownika | `ctx/foundation/prd.md:70` | `BE/Import/DedupHash.cs:11-23`; `ImportParseRow.IsDuplicate` `BE/Import/ImportContracts.cs:10`; ręcznie: 409 `BE/Transactions/TransactionEndpoints.cs:123-135` |
| T-12 | Przegląd duplikatów (keep / skip) | Istniejąca i przychodząca obok siebie, użytkownik decyduje | `ctx/foundation/prd.md:70` | `RowDecision {Keep, Skip}` `BE/Import/ImportContracts.cs:21-26`; ręcznie `Force` `BE/Transactions/TransactionEndpoints.cs:123` |
| T-13 | Kontrola integralności wyciągu | Saldo bieżące musi się sumować, inaczej plik odrzucony | `ctx/foundation/prd.md:66` | `BE/Import/StatementIntegrityException.cs:7`; 422 w `BE/Import/ImportEndpoints.cs:73-82` |
| T-14 | Ostrzeżenie o nakładaniu formatów | Nieblokująca liczba transakcji z innego formatu w tym samym zakresie dat | `ctx/archive/2026-10-02-pdf-import/change.md:14` | `MixedFormatOverlapCount`, `BE/Import/ImportEndpoints.cs:117-129` |
| T-15 | Tylko PLN | Wiersze w innej walucie odfiltrowane i policzone | `ctx/foundation/prd.md:64` | Parsery Erste/VeloBank/mBank PDF (np. `BE/Import/ErsteCsvParser.cs:120-135`); **mBank CSV nie sprawdza waluty** |
| T-16 | Kategoria | Etykieta na transakcji, z listy użytkownika | `ctx/foundation/prd.md:74` | `BE/Categorization/Category.cs:13-26`; per użytkownik od 2026-10-09 |
| T-17 | Rodzaj kategorii (kind) | Wydatek lub przychód | Brak w PRD; `ctx/archive/2026-10-02-category-trend-line-chart/plan.md:11` | `CategoryKind {Expense, Income}` `BE/Categorization/Category.cs:5-9` |
| T-18 | Kolejka kategoryzacji | Transakcje bez kategorii czekające na decyzję | `ctx/foundation/prd.md:51` | Stan wyliczany: `CategoryId == null && !IsInternalTransfer` `BE/Categorization/CategorizationEndpoints.cs:139,155` |
| T-19 | Nieskategoryzowana | Bez kategorii; nie wchodzi do wykresów | `ctx/foundation/prd.md:52` | `Transaction.CategoryId == null` (`BE/Transactions/Transaction.cs:31-32`) |
| T-20 | Przelew wewnętrzny (internal transfer) | Ruch między własnymi rachunkami; oznaczany automatycznie, użytkownik może zmienić | `ctx/foundation/prd.md:76` | `IsInternalTransfer`, `TransferFlagManuallySet` `BE/Transactions/Transaction.cs:36-42`; `BE/Categorization/TransferDetectionService.cs:64-67` |
| T-21 | Okres (period) | Bieżący miesiąc, 30/90 dni, miesiąc, zakres własny | `ctx/foundation/prd.md:84` | `BE/PeriodRange.cs:5-57`; `FE/lib/period.ts:4` |
| T-22 | Sygnał odchylenia od średniej | Powyżej / poniżej / w normie wobec własnej średniej, po ≥1 poprzednim miesiącu | `ctx/foundation/prd.md:91` | `BE/Dashboard/CategoryDeviation.cs:17-68` (`above`/`below`/`inLine`) |
| T-23 | Trend kategorii | Sumy per kategoria w kubełkach tydzień / miesiąc / rok | `ctx/foundation/roadmap.md:238` | `BE/Dashboard/CategoryTrendEndpoints.cs:25` |
| T-24 | Budżet kategorii | Opcjonalny miesięczny budżet (FR-017); **porzucony** | `ctx/foundation/prd.md:93`; porzucenie `ctx/foundation/roadmap.md:274` | **BRAK w kodzie.** Szukałem `budget` (bez rozróżniania wielkości liter) w `BE` i `FE` (bez Migrations/Tests): tylko teksty marketingowe „No budget required” (`FE/components/landing/Hero.tsx:38`, `FE/routes/home.tsx:33`) |
| T-25 | Reguły kategorii (category rules) | Encja wymieniona w kontroli dostępu | `ctx/foundation/prd.md:110` | **BRAK w kodzie.** Szukałem `categoryrule`, `category rule`, `suggest` w `BE` i `FE`: 0 trafień. Zgodne z non-goal `prd.md:117` |
| T-26 | Revolut | Drugi bank z PRD (FR-003); **porzucony** | `ctx/foundation/roadmap.md:272` | **BRAK w kodzie.** Szukałem `revolut`: tylko komentarz w `BE/DI/ImportServiceCollectionExtensions.cs:6` |
| T-27 | Zwrot (refund) | Brak w PRD jako pojęcie | — | Kategoria domyślna „Refunds & Reimbursements” typu Income `BE/Categorization/DefaultCategories.cs:20`; dashboard liczy zwroty jako przychód `BE/Dashboard/DashboardEndpoints.cs:28` |
| T-28 | Transakcja oczekująca (pending) | Płatność VeloBank jeszcze nie zaksięgowana, importowana z datą transakcji | `ctx/foundation/roadmap.md:200,207` | `BE/Import/VeloBankPdfParser.cs:253` |

Pojęcia, które kod ma, a dokumenty nie: `ColorSlot` (stały kolor kategorii na wykresie, `BE/Categorization/Category.cs`), `SortOrder`, lista „Handled” w API (`/categorization/handled`, `BE/Categorization/CategorizationEndpoints.cs:171`), choć UI ją usunął (`ctx/archive/2026-09-30-categorize-queue-only/change.md:14`).

### 1.2 Słowa, które znaczą co innego (granice kontekstów)

| Słowo | Znaczenie A (kontekst) | Znaczenie B (kontekst) | Dowody |
|---|---|---|---|
| **Account** | Konto logowania / tenant (**Identity**) | Rachunek bankowy (**Rachunki i import**) | `ctx/foundation/prd.md:98` („owning user's account”) vs `ctx/foundation/roadmap.md:51`. W kodzie rozdzielone dobrze: `AppUser` vs `Account` |
| **BankName** | Wolna etykieta rachunku nadana przez użytkownika (**Rachunki**) | Kanoniczna nazwa parsera „mBank”/„Erste”/„VeloBank” (**Import**) | `BE/Transactions/Account.cs:12-13` vs `BE/Import/IBankStatementParser.cs:16`; FE pokazuje `bankName` jako „Account name” (`FE/routes/settings.tsx:199-203`), a podsumowanie importu jako bank („Imported N row(s) into {bankName}”, `FE/routes/import.tsx:243`). Unikalny indeks rachunku używa etykiety, nie banku (`BE/AppDbContext.cs:46-48`) |
| **Kind / spend / expense** | Rodzaj kategorii Expense/Income (**Kategoryzacja**) | Kierunek przepływu „spend”/„income” po znaku kwoty (**Wgląd w wydatki**) | `BE/Categorization/Category.cs:5-9` vs `BE/Dashboard/CategoryTrendEndpoints.cs:36-40`; dashboard nie używa `Kind` w ogóle (`BE/Dashboard/DashboardEndpoints.cs:66-71`) |
| **Duplicate** | Kolizja hasha transakcji do przeglądu (**Import**) | Ten sam rachunek albo ta sama nazwa kategorii (**Rachunki**, **Kategoryzacja**) | `BE/Import/ImportContracts.cs:10` vs `BE/Transactions/AccountEndpoints.cs:41-48` |
| **Transfer** | Przelew wewnętrzny między własnymi rachunkami, wykluczany z wydatków (**Kategoryzacja / Wgląd**) | Zwykły przelew wychodzący (BLIK P2P), który jest wydatkiem (**Import**, opis z banku) | `ctx/foundation/prd.md:76` vs `ctx/archive/2026-09-25-mbank-import-with-dedup/research.md:144` |
| **Date** | „Data transakcji” w całej aplikacji (**Wgląd**) | Zależna od banku: data księgowania (mBank) albo operacji (Erste, VeloBank) (**Import**) | `BE/Import/MBankCsvParser.cs:154-157` vs `BE/Import/ErstePdfParser.cs:19`; ten sam przelew może się różnić o kilka dni (`ctx/archive/2026-10-01-erste-import/plan-brief.md:54`) |
| **Skipped** | Wiersz błędny lub w obcej walucie (`SkippedErrorCount`, **Import/parser**) | Świadoma decyzja użytkownika „Skip” (`SkippedDuplicateCount`, **Import/przegląd**) | `BE/Import/MBankCsvParser.cs:175`, `BE/Import/ErsteCsvParser.cs:130-134` vs `BE/Import/ImportEndpoints.cs:174` |
| **Average** | Zwykła średnia historyczna (PRD) | Średnia „do dnia” z korektą tempa, albo średnia z poprzednich równych okien | `ctx/foundation/prd.md:91` vs `BE/Dashboard/CategoryDeviation.cs:3,43` |
| **Import / Queue / Dashboard w UI** | Jedno pojęcie, trzy nazwy | „Import transactions” / „Import statement” / „Import a bank statement”; kolejka to w UI „Up next” / „Remaining” | `FE/components/AppHeader.tsx:18`, `FE/routes/import.tsx:231`, `FE/routes/home.tsx:801`; `FE/routes/categorize.tsx:359,424` |

Najważniejsza granica w kodzie przebiega między **Importem** (język banku: data księgowania, saldo bieżące, opis z wyciągu, „skipped”) a **Wglądem w wydatki** (język użytkownika: wydatek, przychód, kategoria, okres). Te dwa konteksty dzielą dziś jedną encję `Transaction` i jedno słowo „date”.

## Krok 2 — Subdomeny

| Obszar | Kategoria | Uzasadnienie (cele produktu) |
|---|---|---|
| Kategoryzacja (kolejka, ręczny wybór, rekategoryzacja) | **Core** | Wizja: „each category choice is a deliberate call the user makes themselves” (`ctx/foundation/prd.md:22`); wedge (`ctx/foundation/roadmap.md:31`) |
| Wgląd w wydatki: donut, sygnał odchylenia, trend, okresy | **Core** | Logika biznesowa produktu to odchylenie od własnej średniej (`ctx/foundation/prd.md:104`); kryterium sukcesu Primary (`prd.md:33`) |
| Przelewy wewnętrzne (wykrywanie, ręczna decyzja) | **Core (część kategoryzacji)** | Bez tego sumy są zafałszowane: „so income/outflow totals aren't distorted” (`ctx/foundation/prd.md:76`) |
| Import wyciągów z dedup i kontrolą integralności | **Supporting, krytyczny** | North star i największe ryzyko (`ctx/foundation/roadmap.md:35`); Guardrail „never silently double-counted” (`prd.md:39`). Nie jest przewagą produktu, ale bez niego Core liczy złe dane |
| Rachunki bankowe użytkownika | **Supporting** | Potrzebne do wykrywania przelewów i dedup (`prd.md:76`) |
| Parsowanie konkretnych formatów banków (CSV, PDF) | **Generic → Supporting** | Formaty narzucone przez banki; logika integralności jest jednak specyficzna (`prd.md:66`) |
| Uwierzytelnianie, sesja | **Generic** | ASP.NET Identity; sam PRD rozważa prostszy profil lokalny (`ctx/foundation/prd.md:60-61`) |
| Budżet (FR-017) | **Poza zakresem** | Porzucony 2026-10-02 (`ctx/foundation/roadmap.md:274`) |

## Krok 3 — Kandydaci na agregaty i ich niezmienniki

Statusy: **egzekwowany** (jedno miejsce, nie da się obejść), **deklarowany** (komentarz lub UI, serwer nie pilnuje), **ignorowany**, **przeczy** (kod robi coś odwrotnego).

| ID | Kandydat na agregat | Niezmiennik | Źródło | Status w kodzie |
|---|---|---|---|---|
| I-01 | **Przelew wewnętrzny (para przelewu)** wokół `Transaction` | Transakcja oznaczona jako przelew wewnętrzny to ruch między własnymi rachunkami i nie wchodzi do wydatków; decyzja użytkownika nie jest nadpisywana | `ctx/foundation/prd.md:76`; `ctx/archive/2026-09-25-categorization-queue/plan-brief.md:37` | **Częściowo egzekwowany.** Nietykalność ręcznej decyzji: egzekwowana (`BE/Categorization/TransferDetectionService.cs:19-22`, `BE/Categorization/CategorizationEndpoints.cs:201-206`). Spójność pary: **ignorowana**: detektor tylko ustawia `true`, nigdy nie zdejmuje flagi (`TransferDetectionService.cs:45-49`); usunięcie jednej strony zostawia drugą oznaczoną (`BE/Transactions/TransactionEndpoints.cs:251-253`); edycja kwoty, daty albo rachunku nie przelicza flagi (`TransactionEndpoints.cs:217-222`); para jest dobierana zachłannie od zera przy każdym przebiegu, więc nowy import może „przejąć” partnera (`TransferDetectionService.cs:21-53`). Detekcja działa w żądaniach GET i zapisuje (`TransferDetectionService.cs:55-58`, `CategorizationEndpoints.cs:138`, `BE/Dashboard/DashboardEndpoints.cs:61`) |
| I-02 | **Partia importu (`ImportBatch` + jej transakcje)** | Duplikat nigdy nie jest po cichu policzony podwójnie; każdy wykryty duplikat przechodzi przez decyzję użytkownika | `ctx/foundation/prd.md:39`, `:70` | **Egzekwowany przy parse, deklarowany przy commit.** Parse oznacza `IsDuplicate` po hashu liczonym na serwerze (`BE/Import/ImportEndpoints.cs:84-109`). Commit: komentarz obiecuje, że „every row is re-validated” (`ImportEndpoints.cs:142-144`), ale w praktyce przelicza tylko hash. Wiersze, `SkippedErrorCount` i `SourceFormat` przychodzą od klienta bez sprawdzenia (`:173-176`), nic nie wiąże commitu z wcześniej sparsowanym plikiem, a dwa identyczne wiersze „Keep” w jednym żądaniu zapisują się oba (`:181-199`). **Przeczy** komentarzowi |
| I-03 | **Klasyfikacja transakcji (Transaction: kolejka / kategoria / przelew)** | Transakcja z kategorią ma kategorię zgodną ze znakiem kwoty (przychód ↔ Income, wydatek ↔ Expense) | Brak w PRD; komentarz `BE/Categorization/Category.cs:3-4` | **Deklarowany (tylko UI).** Jedynym strażnikiem jest filtr listy w kliencie (`FE/lib/categories.ts:12-20`). Serwer przyjmuje dowolną własną kategorię dla dowolnego znaku (`BE/Transactions/TransactionEndpoints.cs:110`, `BE/Categorization/CategorizationEndpoints.cs:192-199`). Dashboard klasyfikuje po znaku, nie po `Kind` (`BE/Dashboard/DashboardEndpoints.cs:66-71`), więc kategoria Expense może trafić do donuta przychodów |
| I-04 | **Rozpoznanie transakcji ręcznej w imporcie** | Ręcznie dodana transakcja jest rozpoznana jako ta sama, gdy później przyjdzie w wyciągu | `ctx/foundation/prd.md:82` (FR-010) | **Formalnie egzekwowany, praktycznie nieosiągalny.** Ten sam `DedupHash` dla ręcznej i importowanej (`BE/Import/DedupHash.cs:11-23`, `BE/Transactions/TransactionEndpoints.cs:116`), ale hash zawiera surowy opis i datę. Wpisany ręcznie opis prawie nigdy nie jest identyczny z opisem banku, a data banku zależy od parsera (data księgowania lub operacji). Do potwierdzenia testem z prawdziwym wyciągiem (pytanie P-02) |
| I-05 | **Rachunek bankowy (`Account`)** | Użytkownik nie ma dwóch rachunków z tym samym bankiem i numerem | `ctx/archive/2026-09-25-account-management/plan-brief.md:23` | **Egzekwowany w DB, z dziurami.** Unikalny indeks na `(UserId, BankName, AccountNumber)`, czyli na etykiecie, a nie na banku (`BE/AppDbContext.cs:46-48`). Komunikat mówi o „bank and account number” (`BE/Transactions/AccountEndpoints.cs:47`). Komentarz w `AppDbContext.cs:44-45` obiecuje zamianę naruszenia indeksu na 409, ale POST zapisuje bez `catch` (`AccountEndpoints.cs:57-58`), więc wyścig da 500 |
| I-06 | **Katalog kategorii użytkownika (`Category`)** | Nazwa kategorii jest unikalna w obrębie użytkownika; usunięcie używanej kategorii wymaga zgody i odkategoryzowuje transakcje | `ctx/archive/2026-10-09-custom-categories-and-uncategorized-indicator/change.md:12,19` | Zgoda przy usuwaniu: **egzekwowana** (409 + FK `Restrict`, `BE/AppDbContext.cs:51-55`, `BE/Categorization/CategorizationEndpoints.cs:98-127`). Unikalność nazwy: tylko w kodzie, bez indeksu (`CategorizationEndpoints.cs:59-63`) |
| I-07 | **Użytkownik z domyślnymi kategoriami** | Nowy użytkownik zawsze startuje z domyślnymi kategoriami | `ctx/archive/2026-10-09-custom-categories-and-uncategorized-indicator/change.md:17` | **Naruszalny.** Utworzenie użytkownika (`BE/Auth/AuthEndpoints.cs:33`) i zapis kategorii (`:39-40`) to dwa osobne zapisy bez transakcji |
| I-08 | **Wyciąg (statement) w parserze** | Wyciąg, którego salda się nie sumują, jest odrzucony | `ctx/foundation/prd.md:66` (FR-018) | **Egzekwowany dla PDF** (`StatementIntegrityException` → 422, `BE/Import/ImportEndpoints.cs:73-82`). **Nie dotyczy CSV:** błędne wiersze są pomijane i liczone, a w mBank CSV pierwsza nieczytelna data przerywa parsowanie bez liczenia błędu (`BE/Import/MBankCsvParser.cs:155-161`) |
| I-09 | **Sygnał odchylenia (value object)** | Sygnał pojawia się dopiero po ≥1 poprzednim miesiącu historii; pas „w normie” ±10% | `ctx/foundation/prd.md:91`; `ctx/archive/2026-09-30-category-average-deviation-signal/plan-brief.md:22-23` | **Egzekwowany** w jednym czystym miejscu: `BE/Dashboard/CategoryDeviation.cs:17-68`, z testami w `BE/Tests/Dashboard/CategoryDeviationTests.cs`. Wzorzec, do którego warto dociągnąć resztę |

## Krok 4 — Rozjazdy MODEL vs KOD

| ID | Dokument mówi | Kod robi | Dowód | Co naprawić |
|---|---|---|---|---|
| D-01 | Przelewy wewnętrzne są wykluczane z wydatków, żeby sumy nie były zafałszowane (`ctx/foundation/prd.md:76`) | Flaga przelewu zostaje po usunięciu lub edycji partnera i nigdy nie jest zdejmowana automatycznie, więc wydatek może zniknąć z wykresów na stałe | `BE/Categorization/TransferDetectionService.cs:45-49`; `BE/Transactions/TransactionEndpoints.cs:217-222,251-253` | **Kod** |
| D-02 | Serwer ponownie waliduje każdy wiersz commitu (komentarz `BE/Import/ImportEndpoints.cs:142-144`; `ctx/archive/2026-09-25-mbank-import-with-dedup/plan-brief.md:27`) | Przelicza tylko hash; wiersze, liczniki i format bierze od klienta; brak powiązania z parse; duplikaty wewnątrz jednego żądania przechodzą | `BE/Import/ImportEndpoints.cs:173-199` | **Kod** |
| D-03 | Guardrail: duplikaty nigdy nie są po cichu liczone podwójnie, „regardless of how a transaction originally entered the system” (`ctx/foundation/prd.md:39`) | Ta sama transakcja z CSV i z PDF ma inny hash (inne źródło daty i opisu); kod tylko ostrzega licznikiem nakładania | `BE/Import/ImportEndpoints.cs:117-129` | **Dokument.** Decyzja z 2026-10-02: tylko `SourceFormat` i ostrzeżenie zamiast dedup między formatami (`ctx/archive/2026-10-02-pdf-import/change.md:14`). **Zamknięte 2026-10-16:** guardrail w PRD v4 ma wyjątek dla CSV/PDF |
| D-04 | Ręczna transakcja jest rozpoznana, gdy później przyjdzie w imporcie (FR-010, `ctx/foundation/prd.md:82`) | Hash z surowego opisu i daty; zgodność tylko przy identycznym tekście i dacie | `BE/Import/DedupHash.cs:11-23` | **Decyzja właściciela** (P-02) |
| D-05 | Kategoria zgodna ze znakiem kwoty (komentarz `BE/Categorization/Category.cs:3-4`) | Serwer tego nie sprawdza, pilnuje tylko klient; dashboard ignoruje `Kind` | `FE/lib/categories.ts:12-20`; `BE/Categorization/CategorizationEndpoints.cs:192-199`; `BE/Dashboard/DashboardEndpoints.cs:66-71` | **Kod**, albo decyzja, że `Kind` jest tylko podpowiedzią w UI |
| D-06 | Import: „CSV import only” (non-goal, `ctx/foundation/prd.md:114`) | Import PDF dla trzech banków | `BE/Import/MBankPdfParser.cs`, `ErstePdfParser.cs`, `VeloBankPdfParser.cs` | **Dokument.** FR-018 dodany (`ctx/foundation/prd.md:66`), **Zamknięte 2026-10-16:** non-goal i FR-018 uzgodnione w PRD v4 |
| D-07 | Budżet per kategoria (FR-017, `prd.md:93`) i kryterium Secondary (`prd.md:36`) | Brak | T-24 | **Dokument.** Porzucony 2026-10-02 (`ctx/foundation/roadmap.md:274`). **Zamknięte 2026-10-16:** FR-017 oznaczony w PRD v4 jako dropped |
| D-08 | Revolut jako drugi bank (FR-003) | Brak; zamiast tego VeloBank | T-26 | **Dokument.** Porzucony 2026-10-02 (`ctx/foundation/roadmap.md:272`). **Zamknięte 2026-10-16:** Revolut usunięty z PRD v4 |
| D-09 | Encja „category rules” w kontroli dostępu (`prd.md:110`) | Brak | T-25 | **Dokument.** Sprzeczne z non-goal `prd.md:117`. **Zamknięte 2026-10-16:** encja usunięta z Access Control w PRD v4 |
| D-10 | Odchylenie od „historical average” per tydzień/miesiąc (`prd.md:91`) | Tylko miesiąc z korektą tempa albo średnia z poprzednich równych okien; brak sygnału tygodniowego | `BE/Dashboard/CategoryDeviation.cs:3,43-68` | **Dokument.** Decyzje `ctx/archive/2026-09-30-category-average-deviation-signal/plan-brief.md:21-24` i `ctx/archive/2026-10-02-chart-period-selector/plan-brief.md:25`. **Zamknięte 2026-10-16:** FR-013 opisany w PRD v4 |
| D-11 | Brak systemu powiadomień (`prd.md:120`) | Toast o nieskategoryzowanych transakcjach | `FE/components/AppHeader.tsx:37-50` | **Decyzja właściciela.** Toast w aplikacji to raczej nie „notification system”, ale non-goal tego nie rozróżnia. **Przeniesione 2026-10-16** do Open Questions w PRD v4 |
| D-12 | Rachunek unikalny po banku i numerze (komunikat `BE/Transactions/AccountEndpoints.cs:47`) | Unikalność po etykiecie `BankName`, nie po `Bank` | `BE/AppDbContext.cs:46-48` | **Decyzja właściciela.** Etykieta stała się wolnym tekstem dopiero 2026-10-02 (`ctx/archive/2026-10-02-account-bank-selection/change.md:12-14`); indeks został z poprzedniego modelu |
| D-13 | Wyciąg z niesumującymi się saldami jest odrzucony (FR-018) | CSV nie ma kontroli sald; mBank CSV obcina plik przy pierwszej złej dacie bez sygnału | `BE/Import/MBankCsvParser.cs:155-161` | **Decyzja właściciela.** CSV z mBank nie ma kolumny salda bieżącego? (P-03) |
| D-14 | Lista „Handled” usunięta z /categorize (`ctx/archive/2026-09-30-categorize-queue-only/change.md:14`) | Endpoint `/categorization/handled` nadal istnieje | `BE/Categorization/CategorizationEndpoints.cs:171` | **Kod** (martwy endpoint) |

## Krok 5 — Ranking refaktoru

Wartość = jak blisko Core jest niezmiennik. Ryzyko = jak słabo jest dziś pilnowany i jak łatwo go złamać zwykłym użyciem.

| Miejsce | Kandydat | Wartość | Ryzyko | Gotowe, gdy… |
|---|---|---|---|---|
| **#1** | **I-01 Przelew wewnętrzny** | Wysoka: bezpośrednio zmienia sumy w Core (donut, odchylenie, trend) | Wysokie: łamie się przy zwykłym usunięciu lub edycji transakcji, bez żadnego błędu; logika rozsiana po detektorze, dwóch endpointach zapisu i pięciu endpointach GET | test: po usunięciu jednej strony pary druga ma `IsInternalTransfer == false` (o ile flaga nie była ręczna), i `grep "IsInternalTransfer = "` w `BE` znajduje przypisania tylko w jednym typie (agregacie) |
| #2 | I-02 Partia importu | Wysoka: Guardrail i north star | Średnie: klient to własny SPA jednego użytkownika, ale komentarz obiecuje coś, czego kod nie robi | test: commit z dwoma identycznymi wierszami `Keep` albo z wierszem nieobecnym w sparsowanym pliku jest odrzucony nazwanym błędem |
| #3 | I-03 Kategoria zgodna ze znakiem | Średnia: wpływa na donut przychodów i wydatków | Średnie: jedynym strażnikiem jest UI | test: `PUT /categorization/transactions/{id}` z kategorią Income dla kwoty ujemnej zwraca 400 |
| #4 | I-04 Rozpoznanie ręcznej transakcji | Średnia: FR-010 must-have | Nieznane, do zmierzenia (P-02) | test fixture: ręczna transakcja przepisana z wyciągu mBank jest oznaczona `IsDuplicate` przy imporcie tego wyciągu (albo FR-010 przeformułowany w PRD) |
| #5 | I-05 Rachunek bankowy | Niska | Niskie: wyścig, jeden użytkownik | zapytanie: równoległe POST tego samego rachunku daje 409, nigdy 500 |
| #6 | I-07 Użytkownik z kategoriami | Niska | Niskie | zapytanie `SELECT` użytkowników bez żadnej kategorii zwraca 0 wierszy |
| — | I-09 Sygnał odchylenia | Wysoka | Brak: już jest jednym czystym miejscem | wzorzec referencyjny, bez zmian |

**Dlaczego #1 to przelew wewnętrzny.** To jedyny niezmiennik, który jednocześnie (a) decyduje o liczbach w rdzeniu produktu („is this normal for me”, `ctx/foundation/prd.md:22,76`), (b) łamie się w zwykłym, codziennym użyciu (usunięcie lub poprawka transakcji), a nie tylko przy wyścigu czy złośliwym kliencie, i (c) łamie się po cichu: wydatek znika z wykresów bez błędu i bez śladu w UI. Dodatkowo stan żyje w dwóch luźnych flagach na anemicznej encji, a jego zmiany są rozsiane po `TransferDetectionService`, `CategorizationEndpoints` i `TransactionEndpoints`, a detekcja z zapisem odpala się przy każdym GET.

### Pytania do rozstrzygnięcia (kilka minut każde)

| ID | Pytanie | Kto zna odpowiedź |
|---|---|---|
| P-01 | Czy po usunięciu jednej strony przelewu druga ma wrócić do kolejki, czy zostać przelewem (np. przelew na rachunek spoza aplikacji)? | Właściciel produktu (użytkownik) |
| P-02 | Czy FR-010 ma działać dla ręcznie wpisanych opisów? Jeśli tak, hash bez opisu (data ± tolerancja + kwota + rachunek) czy osobne dopasowanie? | Właściciel produktu |
| P-03 | Czy eksport CSV z mBank i Erste zawiera saldo po operacji, które pozwoliłoby na kontrolę integralności jak w PDF? | Programista z próbką wyciągu |
| P-04 | Czy `Kind` kategorii ma być regułą (serwer odrzuca niezgodny znak), czy tylko podpowiedzią w UI? A zwrot za zakupy: przychód czy pomniejszenie wydatku w kategorii? | Właściciel produktu |
| P-05 | Czy unikalność rachunku ma iść po `Bank` + numer, a etykieta może się powtarzać? | Właściciel produktu |
| P-06 | ~~Czy PRD ma dostać wersję 4 z decyzjami z 2026-10-02 i 2026-10-09?~~ **Rozstrzygnięte 2026-10-16:** PRD v4 i roadmapa v2 zmergowane do `main` (PR #51). | Właściciel produktu |
