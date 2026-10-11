using Microsoft.AspNetCore.Identity;

namespace CQPulse.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
}
