using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vatowiec.Desktop;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;
using static Vatowiec.Tests.InstallationSecurityUiTests;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
public sealed class WorkspaceUiTests
{
    [RealSecurityUiFact]
    public async Task NativeAvalonia_LoginMenuStatusColumnsAndReadOnlyKeyboard()
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
                    var api = new WorkspaceTestApi(); using var model = new MainViewModel(_ => api);
                    var window = new MainWindow(model); lifetime.MainWindow = window;
                    try
                    {
                        window.Show(); await Task.Delay(150);
                        Assert.False(Find<Expander>(window, "ConnectionSettings").IsExpanded);
                        Assert.False(Find<TextBox>(window, "ApiAddress").IsEffectivelyVisible);
                        Find<Expander>(window, "ConnectionSettings").IsExpanded = true; await Task.Delay(120);
                        Assert.True(Find<TextBox>(window, "ApiAddress").IsEffectivelyVisible);
                        using (var personalModel = new MainViewModel(_ => new WorkspaceTestApi(), apiAddressOverride: PersonalApiIdentity.For(Guid.NewGuid())))
                        {
                            var personal = new MainWindow(personalModel); personal.Show(); await Task.Delay(100);
                            Assert.False(Find<Expander>(personal, "ConnectionSettings").IsEffectivelyVisible);
                            Assert.False(Find<TextBox>(personal, "ApiAddress").IsEffectivelyVisible); personal.Close();
                        }
                        Find<TextBox>(window, "Login").Text = "fixture"; Find<TextBox>(window, "Password").Text = "memory-only";
                        Click(window, "LoginButton"); await Until(() => model.IsCompanySelection && !model.IsBusy);
                        model.SelectedCompany = model.Companies[0]; await model.OpenCompany(); await Task.Delay(100);
                        Assert.Equal(4, Find<Menu>(window, "ApplicationMenu").Items.Count);
                        Assert.Contains("fixture", Find<TextBlock>(window, "CurrentUser").Text);
                        Assert.Contains("Firma A", Find<TextBlock>(window, "CurrentCompany").Text);
                        var list = Find<ListBox>(window, "Contractors"); Assert.Single(list.Items);
                        list.SelectedIndex = 0; Key(window, Avalonia.Input.Key.F2); await Until(() => model.IsContractorReadOnly && !model.IsBusy);
                        Assert.True(Find<TextBox>(window, "ContractorName").IsReadOnly);
                        Assert.False(Find<Button>(window, "SaveContractor").IsEffectivelyEnabled);
                        Key(window, Avalonia.Input.Key.S, KeyModifiers.Control); Assert.Equal(0, api.Writes);
                        Key(window, Avalonia.Input.Key.Escape); await Until(() => !model.IsEditing);
                        list.SelectedIndex = 0; Key(window, Avalonia.Input.Key.Enter); await Until(() => model.IsContractorReadOnly && !model.IsBusy);
                        Key(window, Avalonia.Input.Key.Escape); await Until(() => !model.IsEditing);
                        list.SelectedIndex = 0; window.Activate(); await Task.Delay(100);
                        var row = (Control)list.ContainerFromIndex(0)!; Assert.NotNull(row);
                        var position = row.PointToScreen(new Point(50, 15)); SetCursorPos(position.X, position.Y);
                        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                        await Task.Delay(50);
                        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                        await Until(() => model.IsContractorReadOnly && !model.IsBusy);
                        await model.StartContractorEdit(); Assert.False(Find<TextBox>(window, "ContractorName").IsReadOnly);
                        Assert.True(Find<Button>(window, "SaveContractor").IsEffectivelyEnabled);
                        Key(window, Avalonia.Input.Key.Escape); await Until(() => !model.IsEditing);
                        Click(window, "ContractorColumns"); await Until(() => model.IsColumnSettingsOpen && !model.IsBusy);
                        await Until(() => window.GetVisualDescendants().OfType<CheckBox>().Count(x => x.DataContext is ColumnChoice) == model.ColumnChoices.Length);
                        Assert.False(Find<Menu>(window, "ApplicationMenu").IsEffectivelyEnabled);
                        foreach (var box in window.GetVisualDescendants().OfType<CheckBox>().Where(x => x.DataContext is ColumnChoice)) box.IsChecked = false;
                        Assert.DoesNotContain(model.ColumnChoices, c => c.IsSelected);
                        Click(window, "SaveMyColumns"); Assert.Contains("co najmniej", model.Message);
                        var name = window.GetVisualDescendants().OfType<CheckBox>().Single(x => x.DataContext is ColumnChoice c && c.Column.Id == "Name");
                        name.IsChecked = true; Click(window, "SaveMyColumns"); await Until(() => !model.IsBusy);
                        Assert.Equal(new[] { "Name" }, model.ContractorTable.VisibleColumns);
                        Click(window, "CloseColumnSettings");
                        window.Width = 940; window.Height = 650; await Task.Delay(100);
                        var status = Find<Border>(window, "SessionStatusBar"); Assert.True(status.IsEffectivelyVisible);
                        Assert.True(status.PointToScreen(default).Y < window.PointToScreen(new Point(0, window.Bounds.Height)).Y);
                        Directory.CreateDirectory(PersonalLocalPaths.VerificationDirectory);
                        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96)))
                        { bitmap.Render(window); bitmap.Save(Path.Combine(PersonalLocalPaths.VerificationDirectory, "workspace-columns.png"), PngBitmapEncoderOptions.Default); }
                        await model.Logout(); Assert.False(status.IsEffectivelyVisible); Assert.Empty(model.StatusBarText);
                        finished.TrySetResult();
                    }
                    catch (Exception e) { finished.TrySetException(e); }
                    finally { window.Close(); lifetime.Shutdown(); }
                })).StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
            }
            catch (Exception e) { finished.TrySetException(e); }
        })
        { IsBackground = true, Name = "Avalonia workspace verification" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(45)); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }
    private static void Key(Window window, Key key, KeyModifiers modifiers = KeyModifiers.None) => window.RaiseEvent(new KeyEventArgs
    { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    private static T Find<T>(Window window, string id) where T : Control => window.GetVisualDescendants().OfType<T>()
        .Concat(window.GetLogicalDescendants().OfType<T>()).Distinct().Single(c => AutomationProperties.GetAutomationId(c) == id);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr info);
}
