using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Infrastructure.Persistence.SystemConfigurations;

// ADR-044: zlecenia techniczne w bazie System; bez danych firm i bez connection stringów.
internal sealed class CompanyOperationConfiguration : IEntityTypeConfiguration<CompanyOperation>
{
    public void Configure(EntityTypeBuilder<CompanyOperation> builder)
    {
        builder.ToTable("CompanyOperations", "ops", table =>
        {
            table.HasCheckConstraint("CK_CompanyOperations_Kind", "[Kind] IN (N'Provision', N'Backup', N'Delete')");
            table.HasCheckConstraint("CK_CompanyOperations_Stage",
                "[Stage] IN (N'Requested', N'DatabaseCreated', N'SchemaApplied', N'ProfileInitialized', N'BackupWritten', N'BackupVerified', N'CompanyDeactivated', N'ArchiveWritten', N'ArchiveVerified', N'DatabaseDropped', N'Ready')");
            table.HasCheckConstraint("CK_CompanyOperations_Requester",
                "[Kind] = N'Backup' OR [RequestedById] IS NOT NULL");
            table.HasCheckConstraint("CK_CompanyOperations_BackupFile",
                "([Kind] = N'Backup' AND [BackupFileName] IS NOT NULL) OR ([Kind] = N'Provision' AND [BackupFileName] IS NULL) OR " +
                "([Kind] = N'Delete' AND [CreateArchive] IS NOT NULL AND (([CreateArchive] = 1 AND [BackupFileName] IS NOT NULL) OR ([CreateArchive] = 0 AND [BackupFileName] IS NULL)))");
            table.HasCheckConstraint("CK_CompanyOperations_Status",
                "[Status] IN (N'Pending', N'Running', N'Failed', N'NeedsReconciliation', N'Completed', N'Cancelled')");
            table.HasCheckConstraint("CK_CompanyOperations_Attempt", "[Attempt] >= 0");
            table.HasCheckConstraint("CK_CompanyOperations_Lease",
                "([Status] = N'Running' AND [LeaseOwner] IS NOT NULL AND [LeaseExpiresAtUtc] IS NOT NULL) OR ([Status] <> N'Running' AND [LeaseOwner] IS NULL AND [LeaseExpiresAtUtc] IS NULL)");
            table.HasCheckConstraint("CK_CompanyOperations_Completed",
                "([Status] = N'Completed' AND [Stage] = N'Ready' AND [CompletedAtUtc] IS NOT NULL) OR ([Status] <> N'Completed' AND [CompletedAtUtc] IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CompanyName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RequestedByName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(x => x.ServerName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ConnectionProfileKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Stage).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.LastErrorCode).HasMaxLength(64);
        builder.Property(x => x.LeaseOwner).HasMaxLength(128);
        builder.Property(x => x.BackupFileName).HasMaxLength(260);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Ignore(x => x.IsFinished);
        builder.Ignore(x => x.CanRetry);
        builder.Ignore(x => x.CanCancel);
        // Idempotencja: jeden klucz operacji na zlecającego; jedna operacja tworzenia na firmę i bazę.
        builder.HasIndex(x => new { x.RequestedById, x.OperationKey }).IsUnique().HasFilter("[RequestedById] IS NOT NULL");
        builder.HasIndex(x => x.CompanyId).IsUnique().HasFilter("[Kind] = N'Provision'");
        builder.HasIndex(x => new { x.ServerName, x.DatabaseName }).IsUnique().HasFilter("[Kind] = N'Provision'");
        // Najwyżej jedna oczekująca lub wykonywana kopia danej firmy.
        builder.HasIndex(x => x.CompanyId, "IX_CompanyOperations_ActiveBackup").IsUnique()
            .HasFilter("[Kind] = N'Backup' AND [Status] IN (N'Pending', N'Running')");
        // ADR-045: najwyżej jedno nieanulowane usuwanie firmy (operacja zakończona znika razem z firmą).
        builder.HasIndex(x => x.CompanyId, "IX_CompanyOperations_ActiveDelete").IsUnique()
            .HasFilter("[Kind] = N'Delete' AND [Status] <> N'Cancelled'");
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
    }
}

internal sealed class DeletedCompanyConfiguration : IEntityTypeConfiguration<DeletedCompany>
{
    public void Configure(EntityTypeBuilder<DeletedCompany> builder)
    {
        builder.ToTable("DeletedCompanies", "ops", table => table.HasCheckConstraint("CK_DeletedCompanies_Archive",
            "([ArchiveFileName] IS NULL AND [ArchiveSizeBytes] IS NULL AND [ArchiveSha256] IS NULL) OR ([ArchiveFileName] IS NOT NULL AND [ArchiveSizeBytes] > 0 AND [ArchiveSha256] IS NOT NULL)"));
        builder.HasKey(x => x.CompanyId);
        builder.Property(x => x.CompanyId).ValueGeneratedNever();
        builder.Property(x => x.CompanyName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Nip).HasMaxLength(32);
        builder.Property(x => x.Regon).HasMaxLength(14);
        builder.Property(x => x.DeletedByName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ArchiveFileName).HasMaxLength(260);
        builder.Property(x => x.ArchiveSha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
    }
}

internal sealed class CompanyOperationEventConfiguration : IEntityTypeConfiguration<CompanyOperationEvent>
{
    public void Configure(EntityTypeBuilder<CompanyOperationEvent> builder)
    {
        builder.ToTable("CompanyOperationEvents", "ops", table => table.HasTrigger("TR_CompanyOperationEvents_Immutable"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Stage).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(64);
        builder.Property(x => x.ActorKind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ActorName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(48).IsRequired();
        builder.HasIndex(x => new { x.OperationId, x.Id });
        builder.HasOne<CompanyOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.NoAction);
    }
}
