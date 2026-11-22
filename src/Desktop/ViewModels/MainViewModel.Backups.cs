using System.Net.Http;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

// ADR-044 krok 2 / ADR-045: FIRMA → Kopie zapasowe, przypomnienie po otwarciu firmy i polityka instalacji.
// Klient tylko zleca i odczytuje stan; kopii nie można pobrać.
public sealed partial class MainViewModel
{
    public static readonly string[] BackupFrequencyCodes = ["Never", "Daily", "Every7Days", "Every30Days"];
    public static readonly string[] BackupFrequencyNames = ["Nigdy", "Codziennie", "Co 7 dni", "Co 30 dni"];
    public static readonly string[] BackupModeCodes = ["Reminder", "Automatic"];
    public static readonly string[] BackupModeNames = ["Przypomnienie", "Kopia automatyczna na serwerze"];
    public static readonly string[] BackupHours = Enumerable.Range(0, 24).Select(x => $"{x:00}:00").ToArray();

    private bool companyBackupsScreen;
    private CompanyBackupStatusResponse? backupStatus;
    private int backupFrequencyIndex, backupModeIndex, backupHourIndex;
    private Guid backupOperationKey = Guid.NewGuid();
    private bool backupReminderVisible;
    private BackupPolicyResponse? backupPolicy;
    private bool policyUsersMayRequest;
    private string policyRetained = "";

    public bool IsCompanyBackupsScreen => IsWorkspace && companyBackupsScreen;
    public CompanyBackupStatusResponse? BackupStatus => backupStatus;
    public CompanyBackupItem[] Backups => backupStatus?.Backups ?? [];
    public string[] BackupLines => Backups.Select(x => $"{x.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} · " +
        $"{Math.Max(0.1, x.SizeBytes / 1048576.0):0.0} MB · " + (x.IsAutomatic ? "kopia automatyczna" : "zlecił: " + x.RequestedByName)).ToArray();
    public bool HasNoBackups => IsCompanyBackupsScreen && backupStatus != null && backupStatus.Backups.Length == 0;
    public string LastBackupText => backupStatus == null ? "" : backupStatus.LastSuccessAtUtc is { } last
        ? $"Ostatnia udana kopia: {last.ToLocalTime():yyyy-MM-dd HH:mm}" : "Ta firma nie ma jeszcze żadnej kopii.";
    public string RetentionText => backupStatus == null ? "" : $"Na serwerze zostaje {backupStatus.RetainedBackupsPerCompany} najnowszych kopii tej firmy; starsze są usuwane automatycznie. Kopii nie można pobrać przez aplikację.";
    public string LatestBackupOperationText => backupStatus?.LatestOperation is not { } op || op.Status == "Completed" ? ""
        : $"Ostatnie zlecenie kopii: {new CompanyOperationRow(op).Status}" + (op.LastErrorCode == null ? "" : " — " + new CompanyOperationRow(op).Error);
    public bool CanRequestBackup => IsWorkspace && !busy && backupStatus?.CanRequest == true &&
        backupStatus.LatestOperation?.Status is not ("Pending" or "Running");
    public bool CanEditBackupSchedule => IsCompanyBackupsScreen && !busy && backupStatus?.CanManageSchedule == true;
    public bool IsBackupHourVisible => backupModeIndex == 1;
    public int BackupFrequencyIndex { get => backupFrequencyIndex; set { backupFrequencyIndex = value; ChangedAll(); } }
    public int BackupModeIndex { get => backupModeIndex; set { backupModeIndex = value; ChangedAll(); } }
    public int BackupHourIndex { get => backupHourIndex; set { backupHourIndex = value; ChangedAll(); } }
    public bool HasBackupScheduleChanges => backupStatus is { } s && (BackupFrequencyCodes[backupFrequencyIndex] != s.Schedule.Frequency ||
        BackupModeCodes[backupModeIndex] != s.Schedule.Mode || backupHourIndex != s.Schedule.PreferredHour);
    public bool CanSaveBackupSchedule => CanEditBackupSchedule && HasBackupScheduleChanges;
    public string BackupScheduleText => backupStatus?.Schedule.IsDefault == true ? "Ustawienia domyślne instalacji (co 30 dni, przypomnienie)." : "";
    public bool IsBackupReminderVisible => backupReminderVisible && IsBrowsingWorkspace;
    public string BackupReminderText => backupStatus?.LastSuccessAtUtc is { } last
        ? $"Zgodnie z harmonogramem tej firmy czas na kopię zapasową. Ostatnia kopia: {last.ToLocalTime():yyyy-MM-dd HH:mm}."
        : "Zgodnie z harmonogramem tej firmy czas na kopię zapasową. Ta firma nie ma jeszcze żadnej kopii.";

    public Task OpenCompanyBackups() => Run(async () =>
    {
        if (api == null || profile == null) return;
        if (HasUnsavedChanges) { RequestDiscard(() => { ClearNavigationDrafts(); _ = OpenCompanyBackups(); }); return; }
        CancelContractorLoading(); ClearNavigationDrafts();
        companyBackupsScreen = true; backupReminderVisible = false;
        await ReadBackupStatus();
    });

    public Task RefreshCompanyBackups() => Run(ReadBackupStatus);

    private async Task ReadBackupStatus()
    {
        if (api == null || profile == null) return;
        AcceptBackupStatus(await api.CompanyBackups(profile.CompanyId));
    }

    private void AcceptBackupStatus(CompanyBackupStatusResponse value)
    {
        if (profile == null || value.CompanyId != profile.CompanyId) throw new ApiException(502, "API zwróciło kopie innej firmy. Odmowa wyświetlenia.");
        backupStatus = value;
        backupFrequencyIndex = Math.Max(0, Array.IndexOf(BackupFrequencyCodes, value.Schedule.Frequency));
        backupModeIndex = Math.Max(0, Array.IndexOf(BackupModeCodes, value.Schedule.Mode));
        backupHourIndex = Math.Clamp(value.Schedule.PreferredHour, 0, 23);
    }

    public Task RequestCompanyBackup() => !CanRequestBackup ? Task.CompletedTask : Run(async () =>
    {
        if (api == null || profile == null) return;
        var operation = await api.RequestCompanyBackup(profile.CompanyId, new(backupOperationKey));
        backupOperationKey = Guid.NewGuid(); backupReminderVisible = false;
        await ReadBackupStatus();
        message = operation.Status is "Pending" or "Running"
            ? "Przyjęto zlecenie kopii. Kopię na serwerze wykonuje usługa VATowiec Operations; odśwież widok, aby zobaczyć wynik."
            : "Zlecenie kopii zostało już przyjęte wcześniej.";
    });

    public Task SaveBackupSchedule() => !CanSaveBackupSchedule ? Task.CompletedTask : Run(async () =>
    {
        if (api == null || profile == null || backupStatus == null) return;
        AcceptBackupStatus(await api.SaveBackupSchedule(profile.CompanyId, new(BackupFrequencyCodes[backupFrequencyIndex],
            BackupModeCodes[backupModeIndex], backupHourIndex, backupStatus.Schedule.RowVersion)));
        message = "Zapisano harmonogram kopii tej firmy.";
    });

    public void CloseCompanyBackups()
    {
        if (busy) return;
        ResetCompanyBackups(); ChangedAll(); _ = LoadContractors();
    }

    private void ResetCompanyBackups()
    {
        companyBackupsScreen = false; backupFrequencyIndex = backupModeIndex = backupHourIndex = 0; backupOperationKey = Guid.NewGuid();
    }

    // Po otwarciu firmy: ciche sprawdzenie, czy wg harmonogramu należy przypomnieć o kopii. Błąd nie przerywa pracy.
    private async Task CheckBackupReminder(Guid companyId)
    {
        if (api == null) return;
        try
        {
            var status = await api.CompanyBackups(companyId);
            if (profile?.CompanyId != companyId || status.CompanyId != companyId) return;
            backupStatus = status; backupReminderVisible = status.ReminderDue;
            ChangedAll();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException) { }
    }

    public Task BackupFromReminder() => RequestCompanyBackup();

    public Task SnoozeBackupReminder() => Run(async () =>
    {
        if (api == null || profile == null) return;
        backupReminderVisible = false;
        backupStatus = await api.SnoozeBackupReminder(profile.CompanyId);
        message = "Przypomnienie o kopii pojawi się najwcześniej jutro.";
    });

    private void ClearBackupState() { ResetCompanyBackups(); backupStatus = null; backupReminderVisible = false; }

    // Ustawienia instalacji → Firmy: polityka kopii (tylko administrator instalacji).
    public bool HasBackupPolicy => backupPolicy != null;
    public bool PolicyUsersMayRequest { get => policyUsersMayRequest; set { policyUsersMayRequest = value; ChangedAll(); } }
    public string PolicyRetained { get => policyRetained; set { policyRetained = value; ChangedAll(); } }
    public bool CanSaveBackupPolicy => IsCompanyOperationsScreen && !busy && backupPolicy != null &&
        int.TryParse(policyRetained, out var retained) && retained is >= 1 and <= 365 &&
        (policyUsersMayRequest != backupPolicy.UsersMayRequestBackups || retained != backupPolicy.RetainedBackupsPerCompany);

    private async Task ReadBackupPolicy()
    {
        if (api == null) return;
        AcceptBackupPolicy(await api.BackupPolicy());
    }

    private void AcceptBackupPolicy(BackupPolicyResponse value)
    {
        backupPolicy = value; policyUsersMayRequest = value.UsersMayRequestBackups; policyRetained = value.RetainedBackupsPerCompany.ToString();
    }

    public Task SaveBackupPolicy() => !CanSaveBackupPolicy ? Task.CompletedTask : Run(async () =>
    {
        if (api == null || backupPolicy == null) return;
        AcceptBackupPolicy(await api.SaveBackupPolicy(new(policyUsersMayRequest, int.Parse(policyRetained), backupPolicy.RowVersion)));
        message = "Zapisano ustawienia kopii zapasowych instalacji.";
    });
}
