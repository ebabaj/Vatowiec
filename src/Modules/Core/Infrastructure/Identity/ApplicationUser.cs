using Microsoft.AspNetCore.Identity;

namespace Vatowiec.Core.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser() => Id = Guid.NewGuid();
}
