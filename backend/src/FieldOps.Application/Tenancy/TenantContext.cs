using FieldOps.Domain.Organizations;

namespace FieldOps.Application.Tenancy;

public sealed record TenantActor(Guid UserId, Guid SessionId, Guid TenantId, OrganizationRole Role);

// Scoped state set only after the server verifies the current session,
// organization, user, and membership in the database. No request-body binding.
public sealed class TenantContext
{
    public TenantActor? Actor { get; private set; }
    public Guid CurrentTenantId => Actor?.TenantId ?? Guid.Empty;

    public void SetValidated(TenantActor actor)
    {
        if (actor.UserId == Guid.Empty || actor.SessionId == Guid.Empty || actor.TenantId == Guid.Empty || !Enum.IsDefined(actor.Role))
        {
            throw new ArgumentException("Validated tenant actor is incomplete.", nameof(actor));
        }

        Actor = actor;
    }
}
