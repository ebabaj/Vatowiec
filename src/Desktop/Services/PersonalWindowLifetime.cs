using Avalonia.Controls;
using Avalonia.Threading;

namespace Vatowiec.Desktop.Services;

public static class PersonalWindowLifetime
{
    public static void Attach(Window window, IPersonalBackend backend, Func<bool>? handoff = null)
    {
        var confirmed = false; var stopping = false;
        window.Closing += async (_, args) =>
        {
            if (args.Cancel || confirmed || handoff?.Invoke() == true) return;
            args.Cancel = true;
            if (stopping) return;
            stopping = true; window.IsEnabled = false; window.Title = "VATowiec Personal — kończenie zapisów i zamykanie…";
            var graceful = await backend.ShutdownAsync();
            if (!graceful)
            {
                var warning = new Window { Title = "Zamykanie VATowca Personal", Width = 520, Height = 180,
                    Content = new TextBlock { Margin = new(20), TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Text = "Serwer nie potwierdził poprawnego zamknięcia. Przy następnym uruchomieniu sprawdź ostatnią operację zapisu. Nie usuwaj plików bazy ani konfiguracji." } };
                await warning.ShowDialog(window);
            }
            confirmed = true; window.Close();
        };
    }
}
