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
                var backend = new Services.PersonalBackend();
                desktop.MainWindow = new PersonalSetupWindow(backend);
                desktop.Exit += (_, _) => backend.Dispose();
            }
            else desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
