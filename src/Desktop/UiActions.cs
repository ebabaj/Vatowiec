using System.Diagnostics;

namespace Vatowiec.Desktop;

/// <summary>
/// Jedyny punkt <c>async void</c> dla obsługi zdarzeń interfejsu (ADR-046).
/// Akcja startuje synchronicznie (np. <c>e.Handled</c> ustawione przed pierwszym await
/// działa jak dotąd), a nieoczekiwany wyjątek trafia do <paramref name="report"/> zamiast
/// zamykać aplikację. Oczekiwane błędy API obsługuje wcześniej model widoku.
/// </summary>
internal static class UiActions
{
    public static async void Run(Func<Task> action, Action<Exception> report)
    {
        try
        {
            await action();
        }
        catch (Exception error)
        {
            // Bez treści wyjątku: komunikaty mogą zawierać dane z formularza.
            Trace.TraceError("VATowiec UI action failed: {0}", error.GetType().FullName);
            report(error);
        }
    }
}
