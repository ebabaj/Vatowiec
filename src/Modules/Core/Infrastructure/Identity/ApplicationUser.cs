using Microsoft.AspNetCore.Identity;

namespace Vatowiec.Core.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser() => Id = Guid.NewGuid();
    public bool IsBlocked { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTimeOffset? PasswordChangedAtUtc { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }
    public Guid? ArchivedByUserId { get; set; }
    public string? ArchivedByName { get; set; }
    public string? HistoricalUserName { get; set; }
    public string? HistoricalNormalizedUserName { get; set; }
    public bool IsLoginReleased { get; set; }
}
