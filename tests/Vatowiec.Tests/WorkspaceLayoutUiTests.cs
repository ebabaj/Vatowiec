using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vatowiec.Desktop;
using Vatowiec.Desktop.ViewModels;
using static Vatowiec.Tests.InstallationSecurityUiTests;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
public sealed class WorkspaceLayoutUiTests
{
    [RealSecurityUiFact]
    public async Task Network_FixedHeaderWithThreeAndFifteenColumnsAt9001200AndMaximized()
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
                    using var model = new MainViewModel(_ => new WorkspaceTestApi()) { UserName = "fixture" };
                    var window = new MainWindow(model); lifetime.MainWindow = window;
                    try
                    {
                        window.Show(); window.Activate(); window.Topmost = true; await Task.Delay(150);
                        await model.Login("memory-only");
                        model.SelectedCompany = model.Companies[0]; await model.OpenCompany(); await Task.Delay(150);
                        var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec", "Verification", "ADR-042", "fixed-header");
                        Directory.CreateDirectory(output);
                        var measurements = new List<object>();
                        foreach (var (width, state, scenario) in new[]
                        {
                            (900, WindowState.Normal, "900"), (1200, WindowState.Normal, "1200"),
                            (1200, WindowState.Maximized, "maximized"), (900, WindowState.Normal, "restored-900")
                        })
                        {
                            window.WindowState = WindowState.Normal;
                            window.Width = width; window.Height = 760; window.WindowState = state; await Task.Delay(200);
                            Assert.Equal(state, window.WindowState);
                            await VerifyHeader(window, model, "ContractorColumns", output, scenario, measurements);
                            await model.OpenCurrencies(settings: true); await Task.Delay(100);
                            await VerifyHeader(window, model, "CurrencyColumns", output, scenario, measurements);
                            model.ShowCurrencySettings(false); await Task.Delay(100);
                            await VerifyHeader(window, model, "RateColumns", output, scenario, measurements);
                            model.CloseCurrencies(); await Task.Delay(100);
                        }
                        File.WriteAllText(Path.Combine(output, "measurements.json"), System.Text.Json.JsonSerializer.Serialize(measurements, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                        await model.Logout(); finished.TrySetResult();
                    }
                    catch (Exception e)
                    {
                        var output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vatowiec", "Verification", "ADR-042", "fixed-header");
                        Directory.CreateDirectory(output); SaveImage(window, Path.Combine(output, "failure.png"));
                        finished.TrySetException(e);
                    }
                    finally { window.Close(); lifetime.Shutdown(); }
                })).StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
            }
            catch (Exception e) { finished.TrySetException(e); }
        }) { IsBackground = true, Name = "Network layout verification" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    private static async Task VerifyHeader(Window window, MainViewModel model, string id, string output, string scenario, List<object> measurements)
    {
        var button = Find<Button>(window, id);
        var table = button.GetVisualAncestors().OfType<ConfigurableTable>().Single();
        var heading = table.GetVisualDescendants().OfType<TableCells>().First();
        var body = table.Children.OfType<ScrollViewer>().Single();
        var headerScroll = heading.GetVisualAncestors().OfType<ScrollViewer>().First();
        // Check the previous configuration immediately after resizing, before saving another one.
        Assert.Equal(table.Bounds.Width, button.TranslatePoint(default, table)!.Value.X + button.Bounds.Width, 1);
        foreach (var requestedCount in new[] { 3, 15 })
        {
            PointerClick(button); await Until(() => model.IsColumnSettingsOpen && !model.IsBusy);
            var count = Math.Min(requestedCount, model.ColumnChoices.Length);
            for (var i = 0; i < model.ColumnChoices.Length; i++) model.ColumnChoices[i].IsSelected = i < count;
            Click(window, "SaveMyColumns"); await Until(() => !model.IsBusy);
            Click(window, "CloseColumnSettings"); await Task.Delay(100);
            Assert.Equal(count, table.Columns!.Length);
            Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
            Assert.Single(table.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content, "⚙"));
            var maximum = Math.Max(0, body.Extent.Width - body.Viewport.Width);
            var scrollbar = body.GetVisualDescendants().OfType<ScrollBar>().Single(x => x.TemplatedParent == body && x.Orientation == Avalonia.Layout.Orientation.Horizontal);
            Assert.Equal(maximum > 0, scrollbar.IsVisible);
            foreach (var offset in new[] { 0d, maximum / 2, maximum })
            {
                body.Offset = new Vector(offset, 0); await Task.Delay(80);
                Assert.Equal(body.Offset.X, headerScroll.Offset.X, 1);
                Assert.Equal(body.Viewport.Width, headerScroll.Viewport.Width, 1);
                var bounds = button.TranslatePoint(default, table)!.Value;
                Assert.Equal(table.Bounds.Width, bounds.X + button.Bounds.Width, 1);
                Assert.InRange(bounds.Y, 0, body.TranslatePoint(default, table)!.Value.Y - button.Bounds.Height);
                Assert.True(headerScroll.TranslatePoint(default, table)!.Value.X + headerScroll.Bounds.Width <= bounds.X + 1);
                Assert.True(body.TranslatePoint(default, table)!.Value.Y >= bounds.Y + button.Bounds.Height);
                var header = Assert.IsType<Grid>(headerScroll.Parent);
                Assert.Equal(table.Bounds.Width, header.Bounds.Width, 1);
                if (id == "ContractorColumns") Assert.NotNull(header.Background);

                var dataRows = table.GetVisualDescendants().OfType<TableCells>().Where(x => x.Value != null).ToArray();
                Assert.NotEmpty(dataRows);
                foreach (var row in dataRows)
                {
                    Assert.Equal(heading.Children.Count, row.Children.Count);
                    for (var i = 0; i < heading.Children.Count; i++)
                        Assert.Equal(heading.Children[i].TranslatePoint(default, table)!.Value.X, row.Children[i].TranslatePoint(default, table)!.Value.X, 1);
                }
                measurements.Add(new { Table = id, Scenario = scenario, WindowWidth = window.Bounds.Width, Columns = count,
                    Offset = body.Offset.X, TableWidth = table.Bounds.Width, GearRight = bounds.X + button.Bounds.Width,
                    HeaderWidth = header.Bounds.Width, Viewport = body.Viewport.Width });
                SaveImage(window, Path.Combine(output, $"{id}-{scenario}-{count}-{offset:0}.png"));
            }
        }
        PointerClick(button); await Until(() => model.IsColumnSettingsOpen && !model.IsBusy);
        Click(window, "CloseColumnSettings"); await Task.Delay(80);
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
