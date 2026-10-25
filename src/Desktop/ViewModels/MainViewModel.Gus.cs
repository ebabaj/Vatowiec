using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool gusSettings;
    private GusConfigurationResponse? gusConfiguration;
    private bool confirmGusDelete;
    private RegistryConfigurationResponse[] registryConfigurations = [];
    private RegistryConfigurationEditor? registrySettingsEditor;
    private int registryServiceIndex;
    public RegistryConfigurationEditor? RegistrySettingsEditor => registrySettingsEditor;
    public int RegistryServiceIndex
    {
        get => registryServiceIndex;
        set
        {
            if (busy) return;
            registryServiceIndex = value;
            SelectRegistryEditor(); ChangedAll();
        }
    }
    public bool IsGusServiceSelected => registryServiceIndex == 0;
    public bool CanSaveRegistryConfiguration => IsGusSettings && !busy && IsInstallationAdministrator && registrySettingsEditor != null;
    public Task RefreshRegistryConfigurations() => Run(ReadRegistryConfigurations);
    private async Task ReadRegistryConfigurations()
    {
        if (api == null || !IsGusSettings) return;
        registryConfigurations = []; registrySettingsEditor = null; ChangedAll();
        registryConfigurations = await api.RegistryConfigurations();
        SelectRegistryEditor();
    }
    private void SelectRegistryEditor()
    {
        var source = registryServiceIndex switch { 1 => "MF", 2 => "VIES", _ => "GUS" };
        var row = registryConfigurations.SingleOrDefault(x => x.Source == source);
        registrySettingsEditor = row == null ? null : new RegistryConfigurationEditor(row);
    }
    public Task SaveRegistryConfiguration()
    {
        if (!CanSaveRegistryConfiguration) return Task.CompletedTask;
        return Run(async () =>
        {
            var editor = registrySettingsEditor!;
            if (editor.SelectedEnvironment is not (0 or 1)) throw new ArgumentException("Wybierz TEST lub PRODUKCJA.");
            if (editor.IsProduction && !editor.ConfirmProduction)
                throw new ArgumentException("Zaznacz świadome potwierdzenie PRODUKCJI dla tej usługi.");
            var result = await api!.SaveRegistryConfiguration(editor.Source, editor.Request());
            if (result.Source != editor.Source) throw new ArgumentException("Odpowiedź dotyczy innej usługi. Odśwież ustawienia.");
            registryConfigurations = registryConfigurations.Select(x => x.Source == result.Source ? result : x).ToArray();
            registrySettingsEditor = new(result);
            message = "Zapisano ustawienia " + editor.Name + ". Obowiązują dla nowych żądań bez restartu API. Nie wykonano zapytania do rejestru.";
        });
    }
    public bool IsInstallationAdministrator => session?.IsInstallationAdministrator == true;
    public bool IsGusSettings => session != null && gusSettings && !IsOwnPasswordChange;
    public bool CanOpenGusSettings => IsInstallationAdministrator && !busy && !IsOwnPasswordChange;
    public bool CanManageGus => IsGusSettings && IsInstallationAdministrator && !busy && gusConfiguration != null;
    public bool CanDeleteGusKey => CanManageGus && gusConfiguration?.IsConfigured == true;
    public bool ConfirmGusDelete => confirmGusDelete;
    public string GusStatus => gusConfiguration == null ? "Stan niepobrany" : gusConfiguration.IsConfigured ? "Skonfigurowano" : "Nie skonfigurowano";
    public string GusEnvironment => "Środowisko GUS: " + (registryConfigurations.FirstOrDefault(x => x.Source == "GUS")?.Environment ?? gusConfiguration?.Environment ?? "niepobrane") +
        ". Klucz produkcyjny jest używany wyłącznie w Production. TEST/MOCK nie potwierdzają danych produkcyjnych.";
    public Task OpenGusSettings()
    {
        if (!CanOpenGusSettings) return Task.CompletedTask;
        if (HasUnsavedChanges) { RequestDiscard(() => { ClearNavigationDrafts(); _ = OpenGusSettings(); }); return Task.CompletedTask; }
        CancelContractorLoading(); ClearNavigationDrafts();
        return Run(async () => { gusSettings = true; await ReadRegistryConfigurations(); await ReadGusStatus(); });
    }
    public Task RefreshGusStatus() => Run(ReadGusStatus);
    private async Task ReadGusStatus()
    {
        if (api == null || !IsGusSettings) return;
        gusConfiguration = null; confirmGusDelete = false;
        AcceptGusStatus(await api.GusConfiguration());
    }
    private void AcceptGusStatus(GusConfigurationResponse value) { gusConfiguration = value; confirmGusDelete = false; }
    public Task SaveGusKey(string key)
    {
        if (!CanManageGus) return Task.CompletedTask;
        return Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Wpisz produkcyjny klucz GUS uzyskany dla tej instalacji.");
            AcceptGusStatus(await api!.SaveGusKey(new() { ApiKey = key, RowVersion = gusConfiguration!.RowVersion }));
            message = "Zapisano zaszyfrowany klucz GUS dla całej instalacji. Nie wykonano zapytania do rejestru.";
        });
    }
    public void RequestGusDelete() { if (CanDeleteGusKey) { confirmGusDelete = true; ChangedAll(); } }
    public void CancelGusDelete() { confirmGusDelete = false; ChangedAll(); }
    public Task DeleteGusKey()
    {
        if (!CanDeleteGusKey || !confirmGusDelete) return Task.CompletedTask;
        return Run(async () =>
        {
            AcceptGusStatus(await api!.DeleteGusKey(new(gusConfiguration!.RowVersion, true)));
            message = "Usunięto wspólny klucz GUS. Produkcyjne zapytania wszystkich firm wymagają ponownej konfiguracji.";
        });
    }
    public void CloseGusSettings() { if (!busy) { ResetGusSettings(); ChangedAll(); _ = LoadContractors(); } }
    private void ResetGusSettings() { gusSettings = false; gusConfiguration = null; confirmGusDelete = false; registryConfigurations = []; registrySettingsEditor = null; registryServiceIndex = 0; }
}
