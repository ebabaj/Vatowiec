using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;

namespace Vatowiec.Core.Infrastructure.Persistence.SystemConfigurations;

// ADR-044 krok 2: metadane kopii, polityka instalacji, harmonogramy firm. Bez ścieżek bezwzględnych i sekretów.
internal sealed class CompanyBackupConfiguration : IEntityTypeConfiguration<CompanyBackup>
{
    public void Configure(EntityTypeBuilder<CompanyBackup> builder)
    {
        builder.ToTable("CompanyBackups", "ops", table =>
        {
            table.HasCheckConstraint("CK_CompanyBackups_Kind", "[Kind] IN (N'Regular', N'PreRestore')");
            table.HasCheckConstraint("CK_CompanyBackups_Size", "[SizeBytes] > 0");
            table.HasCheckConstraint("CK_CompanyBackups_Removed",
                "([RemovedAtUtc] IS NULL AND [RemovedReason] IS NULL) OR ([RemovedAtUtc] IS NOT NULL AND [RemovedReason] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        builder.Property(x => x.RequestedByName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RemovedReason).HasMaxLength(32);
        builder.HasIndex(x => x.OperationId).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.RemovedAtUtc, x.CreatedAtUtc });
        builder.HasOne<CompanyOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BackupPolicyConfiguration : IEntityTypeConfiguration<BackupPolicy>
{
    public void Configure(EntityTypeBuilder<BackupPolicy> builder)
    {
        builder.ToTable("BackupPolicy", "ops", table => table.HasCheckConstraint("CK_BackupPolicy_Values",
            "[SingletonKey] = 1 AND [RetainedBackupsPerCompany] BETWEEN 1 AND 365"));
        builder.HasKey(x => x.SingletonKey);
        builder.Property(x => x.SingletonKey).ValueGeneratedNever();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasData(new { SingletonKey = (byte)1, UsersMayRequestBackups = true, RetainedBackupsPerCompany = BackupPolicy.DefaultRetained });
    }
}

internal sealed class CompanyBackupScheduleConfiguration : IEntityTypeConfiguration<CompanyBackupSchedule>
{
    public void Configure(EntityTypeBuilder<CompanyBackupSchedule> builder)
    {
        builder.ToTable("CompanyBackupSchedules", "ops", table =>
        {
            table.HasCheckConstraint("CK_CompanyBackupSchedules_Frequency", "[Frequency] IN (N'Never', N'Daily', N'Every7Days', N'Every30Days')");
            table.HasCheckConstraint("CK_CompanyBackupSchedules_Mode", "[Mode] IN (N'Reminder', N'Automatic')");
            table.HasCheckConstraint("CK_CompanyBackupSchedules_Hour", "[PreferredHour] BETWEEN 0 AND 23");
        });
        builder.HasKey(x => x.CompanyId);
        builder.Property(x => x.CompanyId).ValueGeneratedNever();
        builder.Property(x => x.Frequency).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Mode).HasMaxLength(16).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne<Company>().WithOne().HasForeignKey<CompanyBackupSchedule>(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BackupSettingChangeConfiguration : IEntityTypeConfiguration<BackupSettingChange>
{
    public void Configure(EntityTypeBuilder<BackupSettingChange> builder)
    {
        builder.ToTable("BackupSettingChanges", "ops", table => table.HasTrigger("TR_BackupSettingChanges_Immutable"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.BeforeJson).HasMaxLength(1024);
        builder.Property(x => x.AfterJson).HasMaxLength(1024).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Id });
    }
}

internal sealed class BackupReminderSnoozeConfiguration : IEntityTypeConfiguration<BackupReminderSnooze>
{
    public void Configure(EntityTypeBuilder<BackupReminderSnooze> builder)
    {
        builder.ToTable("BackupReminderSnoozes", "ops");
        builder.HasKey(x => new { x.UserId, x.CompanyId });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class RestoreImportFileConfiguration : IEntityTypeConfiguration<RestoreImportFile>
{
    public void Configure(EntityTypeBuilder<RestoreImportFile> builder)
    {
        builder.ToTable("RestoreImportFiles", "ops", table => table.HasCheckConstraint("CK_RestoreImportFiles_Size", "[SizeBytes] > 0"));
        builder.HasKey(x => x.FileName);
        builder.Property(x => x.FileName).HasMaxLength(260);
        builder.Property(x => x.CompanyName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
    }
}
