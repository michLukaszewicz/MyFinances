---
title: "MyFinances — słownik domeny dla agentów"
created: 2026-10-16
type: glossary
source: "01-domain-distillation.md"
---

# MyFinances — słownik domeny

| Termin | Co znaczy | Nazwa w kodzie | Jak tego NIE nazywać |
|---|---|---|---|
| Użytkownik | Jedyna osoba, właściciel wszystkich danych | `AppUser`, `UserId` | „account”, „konto” |
| Rachunek bankowy | Własny rachunek użytkownika w banku | `Account` | „konto użytkownika”, „login” |
| Nazwa rachunku | Wolna etykieta nadana przez użytkownika | `Account.BankName` | „bank” (to nie jest bank) |
| Bank | Bank z listy polskich banków albo „Other” | `Account.Bank`, `PolishBanks` | „bank name” |
| Wyciąg | Plik CSV lub PDF wyeksportowany z banku | `StatementFormat` | „export file”, „report” |
| Parser banku | Odczyt wyciągu jednego banku w jednym formacie | `IBankStatementParser` | „importer”, „reader” |
| Partia importu | Jeden zatwierdzony import wyciągu na rachunek | `ImportBatch` | „upload”, „session” |
| Transakcja | Datowana kwota ze znakiem i opisem na rachunku | `Transaction` | „operation”, „entry”, „row” (poza parserem) |
| Wydatek | Transakcja z kwotą ujemną | `Amount < 0`, kierunek `spend` | „expense” jako kierunek przepływu (to rodzaj kategorii) |
| Przychód | Transakcja z kwotą dodatnią | `Amount > 0`, kierunek `income` | „credit”, „deposit” |
| Rodzaj kategorii | Czy kategoria jest wydatkowa czy przychodowa | `CategoryKind.Expense` / `Income` | „type”, „spend” |
| Kategoria | Etykieta transakcji z listy użytkownika | `Category` | „tag”, „label”, „rule” |
| Kolejka kategoryzacji | Transakcje bez kategorii i nie będące przelewem | `/categorization/queue` | „inbox”, „todo”, „handled” |
| Przelew wewnętrzny | Ruch między dwoma własnymi rachunkami; nie wlicza się do wydatków ani przychodów | `IsInternalTransfer` | „transfer” bez przymiotnika (zwykły przelew do kogoś to wydatek) |
| Ręczna decyzja o przelewie | Wybór użytkownika, którego automatyczna detekcja nie zmienia | `TransferFlagManuallySet` | „override”, „lock” |
| Duplikat | Transakcja o tym samym hashu co istniejąca, do decyzji użytkownika | `IsDuplicate`, `DedupHash` | „collision”, „conflict” w UI |
| Decyzja o duplikacie | Zachowaj albo pomiń wiersz przy imporcie | `RowDecision.Keep` / `Skip`; ręcznie `Force` | „ignore”, „merge” |
| Kontrola integralności | Sprawdzenie, że salda wyciągu się sumują; inaczej odrzucenie | `StatementIntegrityException` | „validation error” |
| Pominięty wiersz | Wiersz błędny lub w innej walucie niż PLN | `SkippedErrorCount` | „skipped duplicate” (to osobny licznik) |
| Nakładanie formatów | Ten sam zakres dat zaimportowany raz z CSV, raz z PDF | `MixedFormatOverlapCount` | „duplicate” |
| Okres | Zakres dat analizy: miesiąc, 30/90 dni, własny | `PeriodRange` | „filter”, „range” bez kontekstu |
| Sygnał odchylenia | Powyżej / poniżej / w normie wobec własnej średniej kategorii | `CategoryDeviation`, `above` / `below` / `inLine` | „budget”, „alert”, „limit” |
| Trend kategorii | Sumy kategorii w kubełkach tydzień / miesiąc / rok | `CategoryTrend*` | „history”, „chart” |
| Budżet | Porzucony; nie istnieje w produkcie | — | nie używać |
| Kategoria domyślna | Jedna z 14 kategorii kopiowanych nowemu użytkownikowi; po rejestracji zwykła, usuwalna kategoria | `DefaultCategories.CreateFor` | „systemowa”, „globalna kategoria” |
| Kolor kategorii | Stały slot koloru kategorii na wykresach, niezależny od kolejności listy | `Category.ColorSlot`, `chartColor` | „indeks na liście” |
| Wskaźnik nieskategoryzowanych | Kropka na zakładce Categorize i toast, gdy kolejka jest niepusta | `/categorization/queue/count`, `uncategorized:changed` | „notification”, „alert” |
| Transakcja z importu | Transakcja utworzona przy zatwierdzeniu wyciągu; jej hash jest tożsamością nadaną raz i nie zmienia się przy edycji | `ImportBatchId != null`, `Transaction.FromImport` (planowane) | „imported row” bez rozróżnienia od ręcznej, „zsynchronizowana” |
| Ręczny wpis | Transakcja wpisana przez użytkownika; hash odpowiada aktualnie wpisanym wartościom i nie jest dopasowywany do importu | `ImportBatchId == null`, `Transaction.EnteredManually` (planowane) | „manual import”, „własna transakcja” |
| Tożsamość transakcji | Hash służący do wykrywania duplikatów | `Transaction.Hash`, `Transaction.IdentityFor` (planowane) | „checksum”, „klucz” |
