---
applyTo: "src/Modules/Core/Domain/TableView.cs,src/Modules/Core/Infrastructure/Persistence/TableViewLayoutJson.cs,src/Modules/Core/Infrastructure/Persistence/SystemDbContext.cs,src/Server/Api/Security/TableViewEndpoints.cs,src/SharedContracts/TableLayout.cs,src/SharedContracts/TableViews.cs,src/Desktop/ConfigurableTable.cs,src/Desktop/TableCells.cs,src/Desktop/TableDisplayNames.cs,src/Desktop/TableHeaderGestures.cs,src/Desktop/TableStyles.axaml,src/Desktop/ViewModels/MainViewModel.TableViews.cs,src/Desktop/ViewModels/MainViewModel.TableLayoutPersistence.cs,tests/Vatowiec.Tests/TableViewTests.cs,tests/Vatowiec.Tests/TableViewSqlTests.cs,tests/Vatowiec.Tests/TableLayoutTests.cs,tests/Vatowiec.Tests/ColumnLayoutUiTests.cs,tests/Vatowiec.Tests/TableAppearanceUiTests.cs,tests/Vatowiec.Tests/WorkspaceLayoutUiTests.cs"
---

# TableViews

Odpowiedzialność: wspólny mechanizm konfiguracji i prezentacji tabel, używany obecnie przez Contractors i Currencies. Lokalizację plików oraz zależności opisuje [mapa modułów](../../docs/architecture/module-map.md); zachowanie użytkowe określa [dokumentacja TableViews](../../docs/table-views.md).

Ograniczenia: zachowuj identyfikatory tabel i publiczne kontrakty. Mechanizm konfiguracji nie jest właścicielem ani kopią danych biznesowych. Mapowanie TableViews znajduje się obecnie w `SystemDbContext.cs`; zmieniaj tylko jego właściwą część. Wymagania bezpieczeństwa i współbieżności pozostają w nadrzędnym [AGENTS.md](../../AGENTS.md).

Testy: `TableViewTests`, `TableViewSqlTests`, `TableLayoutTests`, `ColumnLayoutUiTests`, `TableAppearanceUiTests`, `WorkspaceLayoutUiTests` w `tests/Vatowiec.Tests`.

Analizuj tylko TableViews; inne moduły sprawdzaj wyłącznie wtedy, gdy konkretna zmiana przekracza tę granicę.
