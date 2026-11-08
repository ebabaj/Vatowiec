---
applyTo: "src/Modules/Core/Domain/Contractor*.cs,src/Modules/Core/Domain/TaxIdentifierScheme.cs,src/Modules/Core/Application/BankAccountPreparation.cs,src/Modules/Core/Application/IBankDirectory.cs,src/Modules/Core/Application/StandardIdentifiers.cs,src/Modules/Core/Application/TaxIdentifiers.cs,src/Modules/Core/Infrastructure/IbanNetBankAccountValidator.cs,src/Modules/Core/Infrastructure/Persistence/Contractor*.cs,src/Modules/Core/Infrastructure/Persistence/CompanyConfigurations/Contractor*.cs,src/Modules/Core/Infrastructure/Registries/EwibBankDirectory.cs,src/Server/Api/Security/Contractor*.cs,src/Server/Api/Security/IdentifierChecks.cs,src/SharedContracts/Contractor*.cs,src/Desktop/ViewModels/MainViewModel.Contractors.cs,src/Desktop/ViewModels/MainViewModel.Details.cs,src/Desktop/ViewModels/MainViewModel.Finance.cs,tests/Vatowiec.Tests/*Contractor*.cs,tests/Vatowiec.Tests/*BankAccount*.cs,tests/Vatowiec.Tests/*StandardIdentifier*.cs"
---

# Contractors

Odpowiedzialność: kartoteka kontrahentów, ich adresy, rachunki, finanse i identyfikatory; ważne pliki i powiązania opisuje [mapa modułów](../../docs/architecture/module-map.md).

Ograniczenia: korzystaj ze wspólnego `IBankAccountValidator`/`IBankDirectory`, a kody walut pobieraj z istniejącego słownika Currencies. Kontrakty, uprawnienia, izolacja firm, audyt i współbieżność podlegają nadrzędnym regułom [AGENTS.md](../../AGENTS.md); nie twórz równoległych mechanizmów.

Testy: `ContractorDesktopTests`, `ContractorIntegrationTests`, `ContractorDetailsIntegrationTests`, `ContractorFinanceTests`, `ContractorFinanceSqlTests`, `BankAccountValidatorTests`, `BankAccountIntegrationTests`, `StandardIdentifierTests`, `StandardIdentifierSqlTests` w `tests/Vatowiec.Tests`.

Analizuj tylko Contractors; inne moduły sprawdzaj wyłącznie wtedy, gdy konkretna zmiana przekracza tę granicę.
