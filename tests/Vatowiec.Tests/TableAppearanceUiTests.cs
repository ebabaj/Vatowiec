using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vatowiec.Desktop;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;
using static Vatowiec.Tests.InstallationSecurityUiTests;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
public sealed class TableAppearanceUiTests
{
    [RealSecurityUiFact]
    public async Task Network_PolishTitlesSharedAppearanceAndBulkCurrencyCheckboxes()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<SecurityTestApp>().UsePlatformDetect().AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
                {
                    var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
                    var api = new WorkspaceTestApi
                    {
                        AdditionalPermissions = ["ManageCurrencies"],
                        CurrencyData = [new(WorkspaceTestApi.A, "EUR", "euro", true, true, false, false, "AAAAAAAAAAE="),
                            new(WorkspaceTestApi.A, "USD", "dolar", false, false, false, false, "AAAAAAAAAAE="),
                            new(WorkspaceTestApi.A, "PLN", "złoty", true, false, false, false, "AAAAAAAAAAE=")]
                    };
                    var writes = new List<CurrencyActivityRequest>();
                    api.CurrencyActivity = value =>
                    {
                        writes.Add(value);
                        api.CurrencyData = [api.CurrencyData[0] with { IsActive = value.IsActive, DownloadA = false },
                            api.CurrencyData[1] with { IsActive = value.IsActive }, api.CurrencyData[2]];
                        return new(WorkspaceTestApi.A, value.IsActive, api.CurrencyData[..2], ["PLN"]);
                    };
                    using var model = new MainViewModel(_ => api) { UserName = "fixture" };
                    var window = new MainWindow(model); lifetime.MainWindow = window;
                    var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec", "Verification", "ADR-042", "table-appearance");
                    Directory.CreateDirectory(output);
                    try
                    {
                        window.Show(); window.Activate(); window.Topmost = true; await Task.Delay(150);
                        // All record lists use either the configurable table or the shared section header.
                        foreach (var list in window.GetLogicalDescendants().OfType<ListBox>())
                        {
                            Assert.Contains(list.GetLogicalAncestors(), x => x is ConfigurableTable || x is HeaderedContentControl section && section.Classes.Contains("tableSection"));
                            Assert.Equal(14, list.FontSize); Assert.Equal("Segoe UI", list.FontFamily.Name);
                        }
                        var sections = window.GetLogicalDescendants().OfType<HeaderedContentControl>().Where(x => x.Classes.Contains("tableSection")).ToArray();
                        Assert.NotEmpty(sections);
                        foreach (var section in sections)
                        {
                            section.ApplyTemplate();
                            var header = section.GetVisualDescendants().OfType<Border>().Single(x => x.TemplatedParent == section && x.Classes.Contains("tableHeader"));
                            Assert.Equal(48, header.Height);
                            Assert.Equal(Color.Parse("#182A7DB1"), Assert.IsAssignableFrom<ISolidColorBrush>(header.Background).Color);
                        }
                        await model.Login("memory-only"); model.SelectedCompany = model.Companies[0]; await model.OpenCompany();
                        foreach (var width in new[] { 900, 1200 })
                        {
                            window.Width = width; await Task.Delay(120);
                            await CheckTable(window, model, "Contractors", "ContractorColumns", "Contractors", output, width);
                            await model.OpenCurrencies(true); await Task.Delay(120);
                            await CheckTable(window, model, "Currencies", "CurrencyColumns", "CurrencyList", output, width);
                            model.ShowCurrencySettings(false); await Task.Delay(120);
                            await CheckTable(window, model, "CurrencyRates", "RateColumns", "RateList", output, width);
                            model.CloseCurrencies(); await Task.Delay(100);
                        }
                        await model.OpenCurrencies(true); await Task.Delay(120);
                        var checks = window.GetVisualDescendants().OfType<CheckBox>().Where(x => x.DataContext is CurrencySelection).ToArray();
                        Assert.Equal(3, checks.Length);
                        var usd = checks.Single(x => ((CurrencySelection)x.DataContext!).Code == "USD");
                        PointerClick(usd); await Until(() => model.VisibleCurrencySelections.Single(c => c.Code == "USD").IsChecked);
                        Assert.Contains("1 z 3", Find<TextBlock>(window, "CurrencySelectionCount").Text);
                        Click(window, "SelectVisibleCurrencies"); Assert.All(model.VisibleCurrencySelections, c => Assert.True(c.IsChecked));
                        Click(window, "ClearCurrencySelection"); Assert.All(model.VisibleCurrencySelections, c => Assert.False(c.IsChecked));
                        Find<TextBox>(window, "CurrencySearch").Text = "USD"; await Task.Delay(80);
                        Click(window, "SelectVisibleCurrencies"); Assert.Equal("USD", Assert.Single(model.VisibleCurrencySelections, c => c.IsChecked).Code);
                        Click(window, "SelectAllCurrencies"); Assert.Contains("3 z 3", model.CurrencySelectionCount);
                        Click(window, "DeactivateCurrencies"); Assert.True(model.CurrencyActivityPrompt); Assert.Empty(writes);
                        Click(window, "CancelCurrencyActivity"); Assert.False(model.CurrencyActivityPrompt); Assert.Empty(writes);
                        Click(window, "DeactivateCurrencies"); Click(window, "ConfirmCurrencyActivity"); await Until(() => !model.IsBusy && !model.CurrencyActivityPrompt);
                        Assert.False(Assert.Single(writes).IsActive); Assert.False(writes[0].All); Assert.Equal(3, writes[0].Currencies.Length);
                        Assert.True(model.CurrencyRows.Single(c => c.Code == "PLN").IsActive);
                        Assert.All(model.CurrencyRows.Where(c => c.Code != "PLN"), c => Assert.False(c.IsActive));
                        Find<TextBox>(window, "CurrencySearch").Text = "";
                        Click(window, "ActivateAllCurrencies"); Assert.True(model.CurrencyActivityPrompt);
                        Click(window, "ConfirmCurrencyActivity"); await Until(() => !model.IsBusy && !model.CurrencyActivityPrompt);
                        Assert.Equal(2, writes.Count); Assert.True(writes[1].All && writes[1].IsActive && writes[1].Confirm);
                        Assert.All(model.CurrencyRows, c => Assert.True(c.IsActive));
                        SaveImage(window, Path.Combine(output, "currency-checkboxes.png"));
                        await model.Logout(); finished.TrySetResult();
                    }
                    catch (Exception e) { SaveImage(window, Path.Combine(output, "failure.png")); finished.TrySetException(e); }
                    finally { window.Close(); lifetime.Shutdown(); }
                })).StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
            }
            catch (Exception e) { finished.TrySetException(e); }
        }) { IsBackground = true, Name = "Network table appearance verification" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(60)); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    private static async Task CheckTable(Window window, MainViewModel model, string tableId, string gearId, string listId, string output, int width)
    {
        var button = Find<Button>(window, gearId);
        var table = button.GetVisualAncestors().OfType<ConfigurableTable>().Single();
        var header = table.Children.OfType<Grid>().Single();
        Assert.Equal(Color.Parse("#182A7DB1"), Assert.IsAssignableFrom<ISolidColorBrush>(header.Background).Color);
        Assert.Equal(48, header.Bounds.Height);
        var cells = header.GetVisualDescendants().OfType<TableCells>().Single();
        Assert.All(cells.Children.OfType<TextBlock>(), label =>
        {
            Assert.Equal(14, label.FontSize); Assert.Equal("Segoe UI", label.FontFamily.Name);
            Assert.Equal(FontWeight.SemiBold, label.FontWeight); Assert.Equal(new Thickness(8, 6), label.Margin);
        });
        var list = Find<ListBox>(window, listId); list.SelectedIndex = 0; await Task.Delay(80);
        var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
        Assert.Equal(32, row.MinHeight);
        var presenter = row.GetVisualDescendants().OfType<ContentPresenter>().Single(x => x.Name == "PART_ContentPresenter" && x.TemplatedParent == row);
        Assert.Equal(Color.Parse("#8CC5E8"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
        // A focused regression check only; the previously verified 72 layout measurements are not repeated.
        Assert.Equal(table.Bounds.Width, button.TranslatePoint(default, table)!.Value.X + button.Bounds.Width, 1);
        PointerClick(button); await Until(() => model.IsColumnSettingsOpen && !model.IsBusy);
        Assert.Equal("Kolumny tabeli: " + TableDisplayNames.For(tableId), Find<TextBlock>(window, "ColumnSettingsTitle").Text);
        foreach (var choice in model.ColumnChoices) choice.IsSelected = choice.Column.Id == model.ColumnChoices[0].Column.Id;
        Click(window, "SaveMyColumns"); await Until(() => !model.IsBusy);
        Click(window, "CloseColumnSettings"); await Task.Delay(80);
        Assert.Single(table.Columns!);
        Assert.Equal(table.Bounds.Width, button.TranslatePoint(default, table)!.Value.X + button.Bounds.Width, 1);
        SaveImage(window, Path.Combine(output, $"{tableId}-{width}.png"));
    }

    private static void SaveImage(Window window, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
    private static void PointerClick(Control control)
    {
        var point = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
        Assert.True(SetCursorPos(point.X, point.Y));
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr info);
}
