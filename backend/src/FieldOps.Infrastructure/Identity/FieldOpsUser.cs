using Microsoft.AspNetCore.Identity;

namespace FieldOps.Infrastructure.Identity;

public sealed class FieldOpsUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
