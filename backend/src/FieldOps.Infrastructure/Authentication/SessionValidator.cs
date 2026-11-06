using FieldOps.Application.Identity;
using FieldOps.Application.Tenancy;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Authentication;

internal sealed class SessionValidator(FieldOpsDbContext database, TimeProvider clock) : ISessionValidator
{
    public async Task<TenantActor?> ValidateSessionAsync(Guid userId, Guid sessionId, Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || sessionId == Guid.Empty || tenantId == Guid.Empty) return null;
        using var system = database.BeginSystemScope();
        var now = clock.GetUtcNow();
        return await (from session in database.AuthSessions.AsNoTracking()
                      join user in database.Users.AsNoTracking() on session.UserId equals user.Id
                      join organization in database.Organizations.AsNoTracking() on session.TenantId equals organization.Id
                      join membership in database.OrganizationMemberships.AsNoTracking()
                          on new { session.UserId, session.TenantId } equals new { membership.UserId, membership.TenantId }
                      where user.Id == userId && session.Id == sessionId && session.TenantId == tenantId &&
                            user.IsActive && user.EmailConfirmed && !string.IsNullOrEmpty(user.PasswordHash) &&
                            organization.IsActive && membership.IsActive && session.RevokedAt == null &&
                            session.ExpiresAt > now && session.SecurityStamp == user.SecurityStamp
                      select new TenantActor(user.Id, session.Id, organization.Id, membership.Role))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
