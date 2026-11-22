namespace Vatowiec.Core.Domain;

// ADR-044: zlecenie operacji technicznej na bazie jednej firmy, wykonywane przez osobny proces.
public static class CompanyOperationKinds
{
    public const string Provision = "Provision";
    public const string Backup = "Backup";
    public const string Delete = "Delete";
    public const string Restore = "Restore";
}

public static class CompanyOperationStages
{
    public const string Requested = "Requested";
    public const string DatabaseCreated = "DatabaseCreated";
    public const string SchemaApplied = "SchemaApplied";
    public const string ProfileInitialized = "ProfileInitialized";
    public const string BackupWritten = "BackupWritten";
    public const string BackupVerified = "BackupVerified";
    public const string CompanyDeactivated = "CompanyDeactivated";
    public const string ArchiveWritten = "ArchiveWritten";
    public const string ArchiveVerified = "ArchiveVerified";
    public const string DatabaseDropped = "DatabaseDropped";
    public const string DatabaseRestored = "DatabaseRestored";
    public const string RestoreVerified = "RestoreVerified";
    public const string SchemaUpgraded = "SchemaUpgraded";
    public const string Switched = "Switched";
    public const string Ready = "Ready";
    public static readonly string[] Provisioning = [Requested, DatabaseCreated, SchemaApplied, ProfileInitialized, Ready];
    public static readonly string[] Backup = [Requested, BackupWritten, BackupVerified, Ready];
    public static readonly string[] Delete = [Requested, CompanyDeactivated, ArchiveWritten, ArchiveVerified, DatabaseDropped, Ready];
    public static readonly string[] Restore = [Requested, DatabaseRestored, RestoreVerified, SchemaUpgraded, Switched, Ready];
    public static string[] For(string kind) => kind switch
    {
        CompanyOperationKinds.Provision => Provisioning,
        CompanyOperationKinds.Backup => Backup,
        CompanyOperationKinds.Delete => Delete,
        CompanyOperationKinds.Restore => Restore,
        _ => throw new ArgumentException("Nieznany rodzaj operacji.", nameof(kind))
    };
}

// Existing: zastąpienie danych istniejącej firmy; Deleted: przywrócenie usuniętej; Import: przeniesienie z innej instalacji.
public static class RestoreModes
{
    public const string Existing = "Existing";
    public const string Deleted = "Deleted";
    public const string Import = "Import";
    public static readonly string[] All = [Existing, Deleted, Import];
}

public static class CompanyOperationStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Failed = "Failed";
    public const string NeedsReconciliation = "NeedsReconciliation";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public sealed class CompanyOperation : ITrackedEntity
{
    public const int MaxAutomaticAttempts = 5;

    private CompanyOperation() { }

    public static CompanyOperation Provision(Guid id, Guid operationKey, Guid companyId, string companyName,
        Guid requestedById, string requestedByName, Guid actorIssuerId, string requestHash,
        string serverName, string connectionProfileKey, string databaseName) => new()
        {
            Id = EntityText.Id(id, nameof(id)),
            Kind = CompanyOperationKinds.Provision,
            OperationKey = EntityText.Id(operationKey, nameof(operationKey)),
            CompanyId = EntityText.Id(companyId, nameof(companyId)),
            CompanyName = EntityText.Required(companyName, 256, nameof(companyName)),
            RequestedById = EntityText.Id(requestedById, nameof(requestedById)),
            RequestedByName = EntityText.Required(requestedByName, 256, nameof(requestedByName)),
            ActorIssuerId = EntityText.Id(actorIssuerId, nameof(actorIssuerId)),
            RequestHash = EntityText.Required(requestHash, 64, nameof(requestHash)),
            ServerName = EntityText.Required(serverName, 128, nameof(serverName)),
            ConnectionProfileKey = EntityText.Required(connectionProfileKey, 128, nameof(connectionProfileKey)),
            DatabaseName = EntityText.Required(databaseName, 128, nameof(databaseName)),
            Stage = CompanyOperationStages.Requested,
            Status = CompanyOperationStatuses.Pending
        };

    // ADR-044 krok 2: kopia jednej firmy do pliku na serwerze. requestedById == null: harmonogram usługi.
    public static CompanyOperation Backup(Guid id, Guid operationKey, Guid companyId, string companyName,
        Guid? requestedById, string requestedByName, Guid actorIssuerId, string requestHash,
        string serverName, string connectionProfileKey, string databaseName, string backupFileName) => new()
        {
            Id = EntityText.Id(id, nameof(id)),
            Kind = CompanyOperationKinds.Backup,
            OperationKey = EntityText.Id(operationKey, nameof(operationKey)),
            CompanyId = EntityText.Id(companyId, nameof(companyId)),
            CompanyName = EntityText.Required(companyName, 256, nameof(companyName)),
            RequestedById = requestedById is { } user ? EntityText.Id(user, nameof(requestedById)) : null,
            RequestedByName = EntityText.Required(requestedByName, 256, nameof(requestedByName)),
            ActorIssuerId = EntityText.Id(actorIssuerId, nameof(actorIssuerId)),
            RequestHash = EntityText.Required(requestHash, 64, nameof(requestHash)),
            ServerName = EntityText.Required(serverName, 128, nameof(serverName)),
            ConnectionProfileKey = EntityText.Required(connectionProfileKey, 128, nameof(connectionProfileKey)),
            DatabaseName = EntityText.Required(databaseName, 128, nameof(databaseName)),
            BackupFileName = EntityText.Required(backupFileName, 260, nameof(backupFileName)),
            Stage = CompanyOperationStages.Requested,
            Status = CompanyOperationStatuses.Pending
        };

    // ADR-045: usunięcie firmy przez administratora instalacji; archiveFileName == null: bez kopii archiwalnej.
    public static CompanyOperation Delete(Guid id, Guid operationKey, Guid companyId, string companyName,
        Guid requestedById, string requestedByName, Guid actorIssuerId, string requestHash,
        string serverName, string connectionProfileKey, string databaseName, string? archiveFileName, DateTimeOffset? notBefore = null) => new()
        {
            NextAttemptAtUtc = notBefore,
            Id = EntityText.Id(id, nameof(id)),
            Kind = CompanyOperationKinds.Delete,
            OperationKey = EntityText.Id(operationKey, nameof(operationKey)),
            CompanyId = EntityText.Id(companyId, nameof(companyId)),
            CompanyName = EntityText.Required(companyName, 256, nameof(companyName)),
            RequestedById = EntityText.Id(requestedById, nameof(requestedById)),
            RequestedByName = EntityText.Required(requestedByName, 256, nameof(requestedByName)),
            ActorIssuerId = EntityText.Id(actorIssuerId, nameof(actorIssuerId)),
            RequestHash = EntityText.Required(requestHash, 64, nameof(requestHash)),
            ServerName = EntityText.Required(serverName, 128, nameof(serverName)),
            ConnectionProfileKey = EntityText.Required(connectionProfileKey, 128, nameof(connectionProfileKey)),
            DatabaseName = EntityText.Required(databaseName, 128, nameof(databaseName)),
            CreateArchive = archiveFileName is not null,
            BackupFileName = archiveFileName is null ? null : EntityText.Required(archiveFileName, 260, nameof(archiveFileName)),
            Stage = CompanyOperationStages.Requested,
            Status = CompanyOperationStatuses.Pending
        };

    // ADR-044 krok 3: odtworzenie z pliku kopii do bazy roboczej, weryfikacja, migracje, przełączenie.
    // previousDatabaseName: nazwa, pod którą zostaje dotychczasowa baza (tylko odtworzenie istniejącej firmy).
    public static CompanyOperation Restore(Guid id, Guid operationKey, Guid companyId, string companyName,
        Guid requestedById, string requestedByName, Guid actorIssuerId, string requestHash, string serverName,
        string connectionProfileKey, string databaseName, string sourceFileName, string mode, string workingDatabaseName,
        string? previousDatabaseName) => new()
        {
            Id = EntityText.Id(id, nameof(id)),
            Kind = CompanyOperationKinds.Restore,
            OperationKey = EntityText.Id(operationKey, nameof(operationKey)),
            CompanyId = EntityText.Id(companyId, nameof(companyId)),
            CompanyName = EntityText.Required(companyName, 256, nameof(companyName)),
            RequestedById = EntityText.Id(requestedById, nameof(requestedById)),
            RequestedByName = EntityText.Required(requestedByName, 256, nameof(requestedByName)),
            ActorIssuerId = EntityText.Id(actorIssuerId, nameof(actorIssuerId)),
            RequestHash = EntityText.Required(requestHash, 64, nameof(requestHash)),
            ServerName = EntityText.Required(serverName, 128, nameof(serverName)),
            ConnectionProfileKey = EntityText.Required(connectionProfileKey, 128, nameof(connectionProfileKey)),
            DatabaseName = EntityText.Required(databaseName, 128, nameof(databaseName)),
            BackupFileName = EntityText.Required(sourceFileName, 260, nameof(sourceFileName)),
            RestoreMode = RestoreModes.All.Contains(mode) ? mode : throw new ArgumentException("Nieznany tryb odtworzenia.", nameof(mode)),
            WorkingDatabaseName = EntityText.Required(workingDatabaseName, 128, nameof(workingDatabaseName)),
            PreviousDatabaseName = mode == RestoreModes.Existing
                ? EntityText.Required(previousDatabaseName ?? "", 128, nameof(previousDatabaseName))
                : previousDatabaseName is null ? null : throw new ArgumentException("Nazwa poprzedniej bazy dotyczy tylko istniejącej firmy.", nameof(previousDatabaseName)),
            Stage = CompanyOperationStages.Requested,
            Status = CompanyOperationStatuses.Pending
        };

    public Guid Id { get; private set; }
    public string Kind { get; private set; } = null!;
    public Guid OperationKey { get; private set; }
    public Guid CompanyId { get; private set; }
    public string CompanyName { get; private set; } = null!;
    public Guid? RequestedById { get; private set; }
    public string? BackupFileName { get; private set; }
    public bool? CreateArchive { get; private set; }
    public string? RestoreMode { get; private set; }
    public string? WorkingDatabaseName { get; private set; }
    public string? PreviousDatabaseName { get; private set; }
    public string RequestedByName { get; private set; } = null!;
    public Guid ActorIssuerId { get; private set; }
    public string RequestHash { get; private set; } = null!;
    public string ServerName { get; private set; } = null!;
    public string ConnectionProfileKey { get; private set; } = null!;
    public string DatabaseName { get; private set; } = null!;
    public string Stage { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int Attempt { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsFinished => Status == CompanyOperationStatuses.Completed;
    public bool CanRetry => Status is CompanyOperationStatuses.Failed or CompanyOperationStatuses.NeedsReconciliation;
    // Usunięcie można anulować, dopóki baza nie została usunięta i operacja nie jest wykonywana.
    public bool CanCancel => Kind == CompanyOperationKinds.Delete &&
        Status is CompanyOperationStatuses.Pending or CompanyOperationStatuses.Failed or CompanyOperationStatuses.NeedsReconciliation &&
        Array.IndexOf(CompanyOperationStages.Delete, Stage) < Array.IndexOf(CompanyOperationStages.Delete, CompanyOperationStages.DatabaseDropped);

    public bool IsClaimable(DateTimeOffset now) =>
        (Status == CompanyOperationStatuses.Pending && (NextAttemptAtUtc is null || NextAttemptAtUtc <= now)) ||
        (Status == CompanyOperationStatuses.Running && LeaseExpiresAtUtc < now);

    public void Claim(string owner, DateTimeOffset now, TimeSpan lease)
    {
        if (!IsClaimable(now)) throw new InvalidOperationException("Operacja nie jest dostępna do wykonania.");
        LeaseOwner = EntityText.Required(owner, 128, nameof(owner));
        LeaseExpiresAtUtc = now + lease;
        Status = CompanyOperationStatuses.Running;
        Attempt++;
        NextAttemptAtUtc = null;
        LastErrorCode = null;
    }

    // Etapy wyłącznie do przodu; ponowne potwierdzenie bieżącego etapu jest dozwolone (wznowienie).
    public void Reach(string stage, DateTimeOffset now, TimeSpan lease)
    {
        RequireRunning();
        var stages = CompanyOperationStages.For(Kind);
        var target = Array.IndexOf(stages, stage);
        var current = Array.IndexOf(stages, Stage);
        if (target < 0 || target < current || target > current + 1)
            throw new InvalidOperationException("Nieprawidłowa kolejność etapów operacji.");
        Stage = stage;
        LeaseExpiresAtUtc = now + lease;
    }

    public void Complete(DateTimeOffset now)
    {
        RequireRunning();
        if (Stage != CompanyOperationStages.Ready) throw new InvalidOperationException("Operacja nie osiągnęła etapu Ready.");
        Status = CompanyOperationStatuses.Completed;
        CompletedAtUtc = now;
        LeaseOwner = null; LeaseExpiresAtUtc = null; NextAttemptAtUtc = null; LastErrorCode = null;
    }

    // Błąd przejściowy: automatyczne ponowienie z narastającą przerwą, potem stan Failed do ręcznego ponowienia.
    public void Fail(string errorCode, DateTimeOffset now)
    {
        RequireRunning();
        LastErrorCode = EntityText.Required(errorCode, 64, nameof(errorCode));
        LeaseOwner = null; LeaseExpiresAtUtc = null;
        if (Attempt >= MaxAutomaticAttempts)
        {
            Status = CompanyOperationStatuses.Failed; NextAttemptAtUtc = null;
        }
        else
        {
            Status = CompanyOperationStatuses.Pending;
            NextAttemptAtUtc = now + TimeSpan.FromMinutes(Math.Min(15, Attempt * 2));
        }
    }

    // Stan fizyczny nie zgadza się z planem: bez automatycznych prób, wymaga decyzji administratora.
    public void RequireReconciliation(string errorCode)
    {
        RequireRunning();
        LastErrorCode = EntityText.Required(errorCode, 64, nameof(errorCode));
        Status = CompanyOperationStatuses.NeedsReconciliation;
        LeaseOwner = null; LeaseExpiresAtUtc = null; NextAttemptAtUtc = null;
    }

    public void Retry(DateTimeOffset now)
    {
        if (!CanRetry) throw new InvalidOperationException("Ponowić można wyłącznie operację zakończoną błędem.");
        Status = CompanyOperationStatuses.Pending;
        NextAttemptAtUtc = now;
        Attempt = 0;
    }

    public void Cancel()
    {
        if (!CanCancel) throw new InvalidOperationException("Anulować można wyłącznie usunięcie firmy przed usunięciem jej bazy.");
        Status = CompanyOperationStatuses.Cancelled;
        NextAttemptAtUtc = null;
    }

    private void RequireRunning()
    {
        if (Status != CompanyOperationStatuses.Running) throw new InvalidOperationException("Operacja nie jest wykonywana.");
    }
}

// Niezmienna historia przejść operacji (trigger SQL i kontrola w SystemDbContext).
public sealed class CompanyOperationEvent
{
    public const string UserActor = "User";
    public const string WorkerActor = "Worker";

    private CompanyOperationEvent() { }

    public CompanyOperationEvent(CompanyOperation operation, DateTimeOffset at, string actorKind, Guid? actorId, string actorName, string action)
    {
        OperationId = operation.Id;
        OccurredAtUtc = at;
        Stage = operation.Stage;
        Status = operation.Status;
        Attempt = operation.Attempt;
        ErrorCode = operation.LastErrorCode;
        ActorKind = actorKind is UserActor or WorkerActor ? actorKind : throw new ArgumentException("Nieznany rodzaj wykonawcy.", nameof(actorKind));
        ActorId = actorId;
        ActorName = EntityText.Required(actorName, 256, nameof(actorName));
        Action = EntityText.Required(action, 48, nameof(action));
    }

    public long Id { get; private set; }
    public Guid OperationId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string Stage { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int Attempt { get; private set; }
    public string? ErrorCode { get; private set; }
    public string ActorKind { get; private set; } = null!;
    public Guid? ActorId { get; private set; }
    public string ActorName { get; private set; } = null!;
    public string Action { get; private set; } = null!;
}
