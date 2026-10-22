using Vatowiec.SharedContracts;
using Vatowiec.Desktop.Services;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool dataPortSettings;
    private DataPortConfigurationResponse? dataPortConfiguration;
    private bool confirmDataPortDelete;
    public bool IsDataPortSettings => IsWorkspace && dataPortSettings;
    public bool CanManageCompanyIntegrations => IsDataPortSettings && !busy && dataPortConfiguration != null && selectedCompany?.Permissions.Contains("ManageCompanyIntegrations") == true;
    public bool CanDeleteDataPortKey => CanManageCompanyIntegrations && dataPortConfiguration?.IsConfigured == true;
    public bool ConfirmDataPortDelete => confirmDataPortDelete;
    public string DataPortStatus => dataPortConfiguration == null ? "Stan niepobrany" : dataPortConfiguration.IsConfigured ? "Skonfigurowano" : "Nie skonfigurowano";
    public string DataPortPermission => selectedCompany?.Permissions.Contains("ManageCompanyIntegrations") == true
        ? "Możesz zapisać, zastąpić lub usunąć klucz tej firmy." : "Zmiana klucza wymaga uprawnienia administratora firmy. Dostęp tylko do stanu konfiguracji.";
    public Task OpenDataPortSettings() => Run(async () =>
    {
        if (api == null || profile == null || IsEditing) return;
        dataPortSettings = true;
        await ReadDataPortStatus();
    });
    public Task RefreshDataPortStatus() => Run(ReadDataPortStatus);
    private async Task ReadDataPortStatus()
    {
        if (api == null || profile == null || !IsDataPortSettings) return;
        dataPortConfiguration = null;
        var value = await api.DataPortConfiguration(profile.CompanyId);
        AcceptDataPortStatus(value);
    }
    private void AcceptDataPortStatus(DataPortConfigurationResponse value)
    {
        if (profile == null || value.CompanyId != profile.CompanyId) throw new ApiException(502, "API zwróciło konfigurację innej firmy. Odmowa wyświetlenia.");
        dataPortConfiguration = value;
        confirmDataPortDelete = false;
    }
    public Task SaveDataPortKey(string key)
    {
        if (!CanManageCompanyIntegrations || dataPortConfiguration == null) return Task.CompletedTask;
        return Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Wpisz nowy klucz API DataPort.");
            AcceptDataPortStatus(await api!.SaveDataPortKey(profile!.CompanyId, new() { ApiKey = key, RowVersion = dataPortConfiguration.RowVersion }));
            message = "Zapisano zaszyfrowany klucz DataPort dla tej firmy. Nie wykonano zapytania do usługi.";
        });
    }
    public void RequestDataPortDelete()
    {
        if (CanDeleteDataPortKey) { confirmDataPortDelete = true; ChangedAll(); }
    }
    public void CancelDataPortDelete() { confirmDataPortDelete = false; ChangedAll(); }
    public Task DeleteDataPortKey()
    {
        if (!CanDeleteDataPortKey || !confirmDataPortDelete) return Task.CompletedTask;
        return Run(async () =>
        {
            AcceptDataPortStatus(await api!.DeleteDataPortKey(profile!.CompanyId, new(dataPortConfiguration!.RowVersion, true)));
            message = "Usunięto klucz DataPort tej firmy.";
        });
    }
    public void CloseDataPortSettings()
    {
        if (busy) return;
        ResetDataPortSettings(); ChangedAll();
    }
    private void ResetDataPortSettings() { dataPortSettings = false; dataPortConfiguration = null; confirmDataPortDelete = false; }
}
