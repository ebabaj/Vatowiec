using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Vatowiec.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Args?.Contains("--personal", StringComparer.Ordinal) == true)
            {
                var single = Services.PersonalSingleInstance.TryAcquire();
                if (single == null)
                {
                    desktop.MainWindow = new Avalonia.Controls.Window
                    {
                        Title = "VATowiec Personal jest już uruchomiony", Width = 500, Height = 180,
                        Content = new Avalonia.Controls.TextBlock { Margin = new(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Text = "VATowiec Personal już działa dla tego użytkownika Windows. Wróć do otwartego okna programu. Drugi serwer nie został uruchomiony." }
                    };
                    base.OnFrameworkInitializationCompleted(); return;
                }
                string? root = null;
                var args = desktop.Args!;
                var rootIndex = Array.IndexOf(args, "--personal-data-root");
                if (rootIndex >= 0 && rootIndex + 1 < args.Length) root = args[rootIndex + 1];
                Services.PersonalBackend backend;
                try { backend = new Services.PersonalBackend(root); }
                catch (ArgumentException)
                {
                    single.Dispose();
                    desktop.MainWindow = new Avalonia.Controls.Window { Title = "Nieprawidłowa konfiguracja Personal", Width = 500, Height = 180,
                        Content = new Avalonia.Controls.TextBlock { Margin = new(24), Text = "Nieprawidłowy katalog danych Personal. Nie uruchomiono serwera ani nie zmieniono danych." , TextWrapping = Avalonia.Media.TextWrapping.Wrap } };
                    base.OnFrameworkInitializationCompleted(); return;
                }
                desktop.MainWindow = new PersonalSetupWindow(backend);
                desktop.Exit += (_, _) => { backend.Dispose(); single.Dispose(); };
            }
            else desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
