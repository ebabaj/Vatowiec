namespace Vatowiec.Desktop.ViewModels;

// ADR-047: wyłącznie nawigacja do ekranu „Certyfikat serwera”; logika ekranu w ServerCertificateViewModel (ADR-046).
public sealed partial class MainViewModel
{
    private ServerCertificateViewModel? serverCertificate;
    public ServerCertificateViewModel? ServerCertificate => serverCertificate;
    public bool IsServerCertificateScreen => session != null && serverCertificate != null;

    public Task OpenServerCertificate()
    {
        if (!IsInstallationAdministrator || busy || IsOwnPasswordChange || api == null) return Task.CompletedTask;
        var screen = new ServerCertificateViewModel(api);
        return Run(async () =>
        {
            gusSettings = false; serverCertificate = screen;
            screen.PropertyChanged += (_, _) => Changed(nameof(HasUnsavedChanges));
            await screen.Load();
        });
    }

    public Task RefreshServerCertificate() => serverCertificate is { } screen && !busy ? Run(screen.Load) : Task.CompletedTask;

    public Task SaveServerCertificate() => serverCertificate is { IsDirty: true } screen && !busy
        ? Run(async () => message = await screen.Save()) : Task.CompletedTask;

    public Task RequestCaReplacement() => serverCertificate is { CanRequestCaReplacement: true } screen && !busy
        ? Run(async () => message = await screen.RequestCaReplacement()) : Task.CompletedTask;

    // Tekst pliku połączenia albo null przy błędzie (komunikat ustawia Run).
    public async Task<string?> ServerConnectionFileText()
    {
        if (serverCertificate is not { } screen || busy) return null;
        string? text = null;
        await Run(async () => text = await screen.ConnectionFileText());
        return text;
    }

    public void ReportServerConnectionFileSaved(string path) { message = "Zapisano plik połączenia: " + path + ". Przekaż go stacjom zaufanym kanałem."; ChangedAll(); }

    public void CloseServerCertificate()
    {
        if (busy) return;
        void Close() { serverCertificate = null; gusSettings = true; ChangedAll(); }
        if (serverCertificate?.IsDirty == true) RequestDiscard(Close); else Close();
    }
}
