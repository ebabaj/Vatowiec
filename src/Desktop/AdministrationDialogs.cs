using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Vatowiec.Desktop.Services;

namespace Vatowiec.Desktop;

internal static class AdministrationDialogs
{
    internal sealed record PasswordChoice(string? Password)
    {
        public override string ToString() => "PasswordChoice [redacted]";
    }
    private static Window Dialog(Window owner, string title) => new()
    {
        Title = title, Width = 520, MaxWidth = 600, SizeToContent = SizeToContent.Height,
        CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
    };
    public static async Task<bool> Confirm(Window owner, string text)
    {
        var window = Dialog(owner, "Potwierdzenie operacji");
        var yes = new Button { Content = "Potwierdzam" }; yes.SetValue(AutomationProperties.AutomationIdProperty, "ConfirmAdministration");
        var no = new Button { Content = "Anuluj", IsCancel = true }; no.SetValue(AutomationProperties.AutomationIdProperty, "CancelAdministration");
        yes.Click += (_, _) => window.Close(true); no.Click += (_, _) => window.Close(false);
        window.Content = new StackPanel { Margin = new Thickness(24), Spacing = 16, Children =
        { new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { no, yes } } } };
        window.Opened += (_, _) => no.Focus(); return await window.ShowDialog<bool>(owner);
    }
    public static async Task<PasswordChoice?> ChooseTemporaryPassword(Window owner, string action, string description)
    {
        var window = Dialog(owner, action);
        var custom = new CheckBox { Content = "Wpisz własne hasło tymczasowe" };
        custom.SetValue(AutomationProperties.AutomationIdProperty, "UseCustomTemporaryPassword");
        var input = new TextBox { PasswordChar = '●', MaxLength = 1024 };
        input.SetValue(AutomationProperties.AutomationIdProperty, "CustomTemporaryPassword");
        var show = new Button { Content = "Pokaż" };
        show.SetValue(AutomationProperties.AutomationIdProperty, "ShowCustomTemporaryPassword");
        show.Click += (_, _) => { var hidden = input.PasswordChar != '\0'; input.PasswordChar = hidden ? '\0' : '●'; show.Content = hidden ? "Ukryj" : "Pokaż"; };
        var customFields = new StackPanel { Spacing = 8, IsVisible = false, Children = { input, show,
            new TextBlock { Text = "Hasło sprawdzą aktualne wymagania instalacji. Wpisana wartość zostanie użyta dokładnie, bez formatowania.", TextWrapping = Avalonia.Media.TextWrapping.Wrap } } };
        custom.IsCheckedChanged += (_, _) =>
        {
            customFields.IsVisible = custom.IsChecked == true;
            if (custom.IsChecked != true) { input.Text = ""; input.PasswordChar = '●'; show.Content = "Pokaż"; }
            else input.Focus();
        };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var accept = new Button { Content = action, IsDefault = true };
        accept.SetValue(AutomationProperties.AutomationIdProperty, "AcceptTemporaryPassword");
        accept.Click += (_, _) =>
        {
            if (custom.IsChecked == true && string.IsNullOrEmpty(input.Text)) { status.Text = "Wpisz własne hasło albo wyłącz tę opcję, aby wygenerować hasło."; input.Focus(); return; }
            var result = new PasswordChoice(custom.IsChecked == true ? input.Text : null);
            input.Text = ""; window.Close(result);
        };
        var cancel = new Button { Content = "Anuluj", IsCancel = true };
        cancel.SetValue(AutomationProperties.AutomationIdProperty, "CancelTemporaryPassword");
        cancel.Click += (_, _) => window.Close();
        window.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children =
        {
            new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new TextBlock { Text = "Domyślnie serwer wygeneruje 16 losowych znaków. Użytkownik musi zmienić hasło przy następnym logowaniu.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            custom, customFields, status, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { cancel, accept } }
        } };
        try { return await window.ShowDialog<PasswordChoice?>(owner); }
        finally { input.Text = ""; window.Content = null; }
    }
    public static async Task TemporaryPassword(Window owner, string login, string password, bool generated = true)
    {
        var window = Dialog(owner, "Jednorazowe hasło tymczasowe");
        // Keep the actual secret out of TextBox/automation values. Only explicit Copy
        // accesses this short-lived local; closing the dialog clears the reference.
        var secret = password;
        var input = new TextBox { Text = "••••••••", PasswordChar = '●', IsReadOnly = true };
        input.SetValue(AutomationProperties.AutomationIdProperty, "TemporaryPassword");
        password = "";
        var copy = new Button { Content = "Kopiuj hasło" }; copy.SetValue(AutomationProperties.AutomationIdProperty, "CopyTemporaryPassword");
        var show = new Button { Content = "Pokaż" }; show.SetValue(AutomationProperties.AutomationIdProperty, "ShowTemporaryPassword");
        var revealed = false;
        show.Click += (_, _) =>
        {
            revealed = !revealed;
            input.PasswordChar = revealed ? '\0' : '●';
            input.Text = revealed ? TemporaryPasswordPresentation.Format(secret, generated) : "••••••••";
            show.Content = revealed ? "Ukryj" : "Pokaż";
        };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        copy.Click += async (_, _) =>
        {
            try { if (window.Clipboard != null) { await window.Clipboard.SetTextAsync(secret); status.Text = "Skopiowano. Hasło znajduje się w schowku systemowym."; } }
            catch { status.Text = "Nie można skopiować do schowka. Spróbuj ponownie przed zamknięciem okna."; }
        };
        var close = new Button { Content = "Zamknij", IsCancel = true, IsDefault = true }; close.Click += (_, _) => window.Close();
        close.SetValue(AutomationProperties.AutomationIdProperty, "CloseTemporaryPassword");
        window.Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children =
        {
            new TextBlock { Text = "Konto: " + login, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new TextBlock { Text = "Hasło jest dostępne tylko teraz. Przekaż je bezpiecznie. Użytkownik musi ustawić własne hasło po zalogowaniu.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            input,
            new TextBlock { Text = generated ? "Grupy po 4 znaki ułatwiają odczyt. Odstępy nie są częścią hasła; kopiowanie pomija odstępy grupowania." : "Wpisane hasło jest zachowane dokładnie. Kopiowanie nie zmienia jego treści.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { show, copy } }, status, close
        } };
        try { await window.ShowDialog(owner); }
        finally { secret = ""; input.Text = ""; window.Content = null; }
    }
}
