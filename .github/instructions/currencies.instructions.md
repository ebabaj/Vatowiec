---
applyTo: "src/Modules/Core/Domain/Currency.cs,src/Modules/Core/Application/ExchangeRates.cs,src/Modules/Core/Infrastructure/Persistence/Currency*.cs,src/Modules/Core/Infrastructure/Persistence/ExchangeRateService.cs,src/Modules/Core/Infrastructure/Registries/NbpClient.cs,src/Server/Api/Security/CurrencyEndpoints.cs,src/SharedContracts/CurrencyContracts.cs,src/Desktop/ViewModels/MainViewModel.Currencies.cs,src/Desktop/Converters/FinanceCurrencyLabelConverter.cs,tests/Vatowiec.Tests/ActiveCurrencyChoiceTests.cs,tests/Vatowiec.Tests/*Currency*.cs,tests/Vatowiec.Tests/NbpTests.cs"
---

# Currencies

Odpowiedzialność: słownik walut i kursy, w tym ręcznie inicjowane pobieranie NBP. Granice obecnego kodu i zależności opisuje [mapa modułów](../../docs/architecture/module-map.md); szczegóły zachowania są w [dokumentacji walut](../../docs/currencies-and-nbp.md).

Ograniczenia: zachowuj istniejące API, publiczne DTO, uprawnienia, schemat i historię danych. NBP odpytuj wyłącznie w istniejących jawnych przepływach; nie dodawaj harmonogramu ani automatycznego doboru kursu podatkowego. ContractorFinance korzysta z istniejącego kodu waluty, nie z osobnego słownika. Nadrzędne wymagania bezpieczeństwa i danych są w [AGENTS.md](../../AGENTS.md).

Testy: `NbpTests`, `ActiveCurrencyChoiceTests`, `CurrencyDesktopTests`, `CurrencyIntegrationTests`, `CurrencyActivitySqlTests` w `tests/Vatowiec.Tests`.

Analizuj tylko Currencies; inne moduły sprawdzaj wyłącznie wtedy, gdy konkretna zmiana przekracza tę granicę.
