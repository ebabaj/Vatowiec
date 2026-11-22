namespace Vatowiec.Core.Domain;

// ADR-044 krok 2 i ADR-045: kopie jednej firmy na serwerze, rotacja i harmonogram.

// Zweryfikowana kopia (plik .bak + manifest). Rotacja oznacza usunięcie pliku, wpis zostaje jako ślad.
public sealed class CompanyBackup
{
    public const string RegularKind = "Regular";

    private CompanyBackup() { }

    public CompanyBackup(Guid companyId, Guid operationId, string fileName, long sizeBytes, string sha256,
        DateTimeOffset createdAtUtc, bool isAutomatic, string requestedByName)
    {
        CompanyId = EntityText.Id(companyId, nameof(companyId));
        OperationId = EntityText.Id(operationId, nameof(operationId));
        FileName = EntityText.Required(fileName, 260, nameof(fileName));
        SizeBytes = sizeBytes > 0 ? sizeBytes : throw new ArgumentException("Pusta kopia.", nameof(sizeBytes));
        Sha256 = sha256.Length == 64 && sha256.All(Uri.IsHexDigit) ? sha256.ToUpperInvariant()
            : throw new ArgumentException("Nieprawidłowa suma SHA-256.", nameof(sha256));
        CreatedAtUtc = createdAtUtc;
        IsAutomatic = isAutomatic;
        RequestedByName = EntityText.Required(requestedByName, 256, nameof(requestedByName));
        Kind = RegularKind;
    }

    public long Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid OperationId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public bool IsAutomatic { get; private set; }
    public string RequestedByName { get; private set; } = null!;
    public DateTimeOffset? RemovedAtUtc { get; private set; }
    public string? RemovedReason { get; private set; }

    public void MarkRemoved(DateTimeOffset at, string reason)
    {
        if (RemovedAtUtc is not null) return;
        RemovedAtUtc = at;
        RemovedReason = EntityText.Required(reason, 32, nameof(reason));
    }
}

// Ustawienia instalacji (jeden wiersz): czy użytkownicy mogą zlecać kopie i ile kopii zachować.
public sealed class BackupPolicy
{
    public const int DefaultRetained = 7;
    private BackupPolicy() { }
    public byte SingletonKey { get; private set; } = 1;
    public bool UsersMayRequestBackups { get; private set; } = true;
    public int RetainedBackupsPerCompany { get; private set; } = DefaultRetained;
    public byte[] RowVersion { get; private set; } = [];

    public void Change(bool usersMayRequestBackups, int retainedBackupsPerCompany)
    {
        if (retainedBackupsPerCompany is < 1 or > 365)
            throw new ArgumentException("Liczba zachowywanych kopii musi mieścić się w zakresie 1–365.", nameof(retainedBackupsPerCompany));
        UsersMayRequestBackups = usersMayRequestBackups;
        RetainedBackupsPerCompany = retainedBackupsPerCompany;
    }
}

public static class BackupFrequencies
{
    public const string Never = "Never";
    public const string Daily = "Daily";
    public const string Every7Days = "Every7Days";
    public const string Every30Days = "Every30Days";
    public static readonly string[] All = [Never, Daily, Every7Days, Every30Days];
    public static int? Days(string frequency) => frequency switch
    {
        Daily => 1,
        Every7Days => 7,
        Every30Days => 30,
        Never => null,
        _ => throw new ArgumentException("Nieznana częstotliwość kopii.", nameof(frequency))
    };
}

public static class BackupModes
{
    public const string Reminder = "Reminder";
    public const string Automatic = "Automatic";
    public static readonly string[] All = [Reminder, Automatic];
}

// Harmonogram jednej firmy. Brak wiersza = ustawienia domyślne (decyzja właściciela: co 30 dni, przypomnienie).
public sealed class CompanyBackupSchedule : ITrackedEntity
{
    public const string DefaultFrequency = BackupFrequencies.Every30Days;
    public const string DefaultMode = BackupModes.Reminder;
    public const int DefaultPreferredHour = 22;

    private CompanyBackupSchedule() { }

    public CompanyBackupSchedule(Guid companyId, string frequency, string mode, int preferredHour)
    {
        CompanyId = EntityText.Id(companyId, nameof(companyId));
        Change(frequency, mode, preferredHour);
    }

    public Guid CompanyId { get; private set; }
    public string Frequency { get; private set; } = null!;
    public string Mode { get; private set; } = null!;
    public int PreferredHour { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Change(string frequency, string mode, int preferredHour)
    {
        if (!BackupFrequencies.All.Contains(frequency)) throw new ArgumentException("Nieznana częstotliwość kopii.", nameof(frequency));
        if (!BackupModes.All.Contains(mode)) throw new ArgumentException("Nieznany tryb kopii.", nameof(mode));
        if (preferredHour is < 0 or > 23) throw new ArgumentException("Godzina kopii musi mieścić się w zakresie 0–23.", nameof(preferredHour));
        Frequency = frequency; Mode = mode; PreferredHour = preferredHour;
    }
}

// Terminy liczone w dniach kalendarzowych czasu lokalnego serwera (bez dryfu godziny wykonania).
public static class BackupScheduleRules
{
    public static bool IsDue(string frequency, DateTimeOffset? lastSuccessUtc, DateTimeOffset companyCreatedUtc,
        DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        if (BackupFrequencies.Days(frequency) is not { } days) return false;
        var since = TimeZoneInfo.ConvertTime(lastSuccessUtc ?? companyCreatedUtc, zone).Date;
        return (TimeZoneInfo.ConvertTime(nowUtc, zone).Date - since).TotalDays >= days;
    }

    public static bool IsAutomaticWindow(int preferredHour, DateTimeOffset nowUtc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(nowUtc, zone).Hour >= preferredHour;

    // „Przypomnij później”: najwcześniej następnego dnia lokalnego.
    public static DateTimeOffset NextLocalDay(DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var midnight = local.Date.AddDays(1);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
    }
}

// Niezmienny audyt zmian polityki instalacji (CompanyId == null) i harmonogramów firm.
public sealed class BackupSettingChange
{
    private BackupSettingChange() { }
    public BackupSettingChange(Guid? companyId, Guid actorId, string actorName, DateTimeOffset atUtc, string? beforeJson, string afterJson)
    {
        CompanyId = companyId;
        ActorId = EntityText.Id(actorId, nameof(actorId));
        ActorName = EntityText.Required(actorName, 256, nameof(actorName));
        AtUtc = atUtc;
        BeforeJson = beforeJson;
        AfterJson = EntityText.Required(afterJson, 1024, nameof(afterJson));
    }
    public long Id { get; private set; }
    public Guid? CompanyId { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = null!;
    public DateTimeOffset AtUtc { get; private set; }
    public string? BeforeJson { get; private set; }
    public string AfterJson { get; private set; } = null!;
}

// Odłożone przypomnienie o kopii — dla konkretnego użytkownika i firmy.
public sealed class BackupReminderSnooze
{
    private BackupReminderSnooze() { }
    public BackupReminderSnooze(Guid userId, Guid companyId, DateTimeOffset untilUtc)
    {
        UserId = EntityText.Id(userId, nameof(userId));
        CompanyId = EntityText.Id(companyId, nameof(companyId));
        UntilUtc = untilUtc;
    }
    public Guid UserId { get; private set; }
    public Guid CompanyId { get; private set; }
    public DateTimeOffset UntilUtc { get; private set; }
    public void Extend(DateTimeOffset untilUtc) { if (untilUtc > UntilUtc) UntilUtc = untilUtc; }
}
