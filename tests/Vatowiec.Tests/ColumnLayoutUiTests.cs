using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vatowiec.Desktop;
using Vatowiec.Desktop.ViewModels;
using static Vatowiec.Tests.InstallationSecurityUiTests;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
public sealed class ColumnLayoutUiTests
{
    [RealSecurityUiFact]
    public async Task Network_NativeMouseResizeReorderAutosaveScrollConfiguratorAndNewWindow()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                AppBuilder.Configure<SecurityTestApp>().UsePlatformDetect().AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
                {
                    var lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
                    var api = new WorkspaceTestApi { AdditionalPermissions = ["ManageCurrencies"] };
                    using var model = new MainViewModel(_ => api) { UserName = "fixture" };
                    var window = new MainWindow(model); lifetime.MainWindow = window;
                    var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec", "Verification", "ADR-042", "column-layout");
                    Directory.CreateDirectory(output);
                    try
                    {
                        window.Show(); window.Topmost = true; window.Activate(); await Task.Delay(120);
                        await model.Login("memory-only"); model.SelectedCompany = model.Companies[0]; await model.OpenCompany(); await Task.Delay(100);
                        foreach (var scenario in new[] { "900", "1200", "maximized" })
                        {
                            window.WindowState = WindowState.Normal; window.Width = scenario == "900" ? 900 : 1200; window.Height = 820;
                            if (scenario == "maximized") window.WindowState = WindowState.Maximized;
                            await Task.Delay(150);
                            await Exercise(window, model, api, "Contractors", "ContractorColumns", output, scenario);
                            await model.OpenCurrencies(true); await Task.Delay(100);
                            await Exercise(window, model, api, "Currencies", "CurrencyColumns", output, scenario);
                            var check = window.GetVisualDescendants().OfType<CheckBox>().Single(x => x.DataContext is CurrencySelection);
                            check.IsChecked = true; Assert.True(Assert.Single(model.VisibleCurrencySelections).IsChecked);
                            model.ShowCurrencySettings(false); await Task.Delay(100);
                            await Exercise(window, model, api, "CurrencyRates", "RateColumns", output, scenario);
                            model.CloseCurrencies(); await Task.Delay(100);
                        }
                        await model.NavigateContractors();
                        var expected = model.ContractorColumns.Select(c => (c.Id, c.Width)).ToArray();
                        window.Hide();
                        using var reopened = new MainViewModel(_ => api) { UserName = "fixture" };
                        var second = new MainWindow(reopened); second.Show(); second.Topmost = true; await Task.Delay(100);
                        await reopened.Login("memory-only"); reopened.SelectedCompany = reopened.Companies[1]; await reopened.OpenCompany(); await Task.Delay(100);
                        Assert.Equal(expected, reopened.ContractorColumns.Select(c => (c.Id, c.Width)).ToArray());
                        second.Close(); window.Show(); await model.Logout(); completion.TrySetResult();
                    }
                    catch (Exception ex) { SaveImage(window, Path.Combine(output, "native-failure.png")); completion.TrySetException(ex); }
                    finally { window.Close(); lifetime.Shutdown(); }
                })).StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "Network native column gestures" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(90)); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }
    private static async Task Exercise(Window window, MainViewModel model, WorkspaceTestApi api, string id, string gearId, string output, string scenario)
    {
        var gear = Find<Button>(window, gearId); var table = gear.GetVisualAncestors().OfType<ConfigurableTable>().Single();
        gear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => model.IsColumnSettingsOpen && !model.IsBusy);
        await Task.Delay(80);
        foreach (var c in model.ColumnChoices) c.IsSelected = true;
        var firstChoice = model.ColumnChoices[0];
        var down = window.GetVisualDescendants().OfType<Button>().Single(x => x.DataContext == firstChoice && Equals(x.Content, "W dół"));
        down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Same(firstChoice, model.ColumnChoices[1]);
        await Task.Delay(60);
        var up = window.GetVisualDescendants().OfType<Button>().First(x => x.DataContext == firstChoice && Equals(x.Content, "W górę"));
        up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(window, "SaveMyColumns"); await Until(() => !model.IsBusy); Click(window, "CloseColumnSettings"); await Task.Delay(120);
        var body = table.Children.OfType<ScrollViewer>().Single(); body.Offset = default; await Task.Delay(60);
        var header = table.Children.OfType<Grid>().Single(); var cells = header.GetVisualDescendants().OfType<TableCells>().Single();
        var column = table.Columns![0]; var width = column.Width!.Value; var otherWidths = table.Columns.Skip(1).Select(c => c.Width).ToArray();
        var writes = api.LayoutWrites.Count; var queries = api.SearchCalls;
        var start = new Point(width - 2, 20); var end = new Point(width + 46, 20);
        await Drag(cells, start, end, async () =>
        {
            Assert.Equal(writes, api.LayoutWrites.Count); Assert.Equal(width + 48, table.Columns![0].Width!.Value, 1);
            await Task.CompletedTask;
        });
        await Until(() => !model.IsTableLayoutSaving && !model.HasPendingTableLayouts);
        Assert.Equal(writes + 1, api.LayoutWrites.Count); Assert.Equal(otherWidths, table.Columns!.Skip(1).Select(c => c.Width).ToArray());
        Assert.Equal(queries, api.SearchCalls);
        var maximum = Math.Max(0, body.Extent.Width - body.Viewport.Width);
        if (scenario != "maximized" || id == "Contractors") Assert.True(maximum > 0);
        body.Offset = new Vector(Math.Min(40, maximum), 0); await Task.Delay(60);
        var viewport = cells.GetVisualAncestors().OfType<ScrollViewer>().First();
        var first = table.Columns[0]; var second = table.Columns[1];
        var secondStart = Math.Min(table.HeaderInset + first.Width!.Value + second.Width!.Value / 2 - body.Offset.X, viewport.Bounds.Width - 15);
        var destination = Math.Max(10, table.HeaderInset + first.Width.Value / 2 - body.Offset.X - 30);
        writes = api.LayoutWrites.Count;
        await Drag(viewport, new Point(secondStart, 20), new Point(destination, 20), async () =>
        {
            Assert.Equal(writes, api.LayoutWrites.Count);
            Assert.True(header.Children.OfType<Border>().Single().IsVisible); await Task.CompletedTask;
        });
        await Until(() => !model.IsTableLayoutSaving && !model.HasPendingTableLayouts);
        Assert.Equal(second.Id, table.Columns![0].Id); Assert.Equal(writes + 1, api.LayoutWrites.Count); Assert.Equal(queries, api.SearchCalls);
        foreach (var offset in new[] { 0d, maximum })
        {
            body.Offset = new Vector(offset, 0); await Task.Delay(60);
            Assert.Equal(body.Offset.X, viewport.Offset.X, 1);
            var row = table.GetVisualDescendants().OfType<TableCells>().First(x => x.Value != null);
            for (var i = 0; i < cells.Children.Count; i++)
                Assert.Equal(cells.Children[i].TranslatePoint(default, table)!.Value.X, row.Children[i].TranslatePoint(default, table)!.Value.X, 1);
            Assert.Equal(table.Bounds.Width, gear.TranslatePoint(default, table)!.Value.X + gear.Bounds.Width, 1);
        }
        viewport.Offset = new Vector(Math.Min(50, maximum), 0); await Task.Delay(60);
        Assert.Equal(viewport.Offset.X, body.Offset.X, 1);
        SaveImage(window, Path.Combine(output, $"{id}-{scenario}.png"));
    }
    private static async Task Drag(Control owner, Point start, Point end, Func<Task> held)
    {
        var from = owner.PointToScreen(start); var to = owner.PointToScreen(end);
        SetCursorPos(from.X, from.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(50);
        try
        {
            for (var step = 1; step <= 5; step++) { SetCursorPos(from.X + (to.X - from.X) * step / 5, from.Y + (to.Y - from.Y) * step / 5); await Task.Delay(35); }
            await held();
        }
        finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
        await Task.Delay(80);
    }
    private static void SaveImage(Window window, string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr info);
}
