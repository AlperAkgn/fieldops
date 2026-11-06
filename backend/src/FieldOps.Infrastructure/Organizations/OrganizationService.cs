using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Common;
using FieldOps.Application.Identity;
using FieldOps.Application.Organizations;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Identity;
using FieldOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Organizations;

public sealed class OrganizationService(FieldOpsDbContext database, TenantContext tenantContext,
    TimeProvider timeProvider, ILookupNormalizer normalizer, IEmailOutbox emailOutbox) : IOrganizationService
{
    public async Task<IReadOnlyList<OrganizationSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        await RequireActiveUserAsync(actor.UserId, false, cancellationToken);
        // Explicit security bootstrap: a user may discover their own verified
        // memberships across tenants, never organizations supplied by a client.
        using var bootstrap = database.BeginSystemScope();
        return await (from membership in database.OrganizationMemberships.AsNoTracking()
                      join organization in database.Organizations.AsNoTracking() on membership.TenantId equals organization.Id
                      where membership.UserId == actor.UserId && membership.IsActive && organization.IsActive
                      orderby organization.Name, organization.Id
                      select new OrganizationSummary(organization.Id, organization.Name, organization.TimeZoneId, membership.Role))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationSummary> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var organization = CreateOrganization(request.Name, request.TimeZoneId, now);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var user = await LockActorUserAsync(true, cancellationToken);
        using var bootstrap = database.BeginSystemScope();
        var membership = OrganizationMembership.Create(organization.Id, user.Id, OrganizationRole.OrganizationOwner, now);
        database.Organizations.Add(organization);
        database.OrganizationMemberships.Add(membership);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OrganizationSummary(organization.Id, organization.Name, organization.TimeZoneId, membership.Role);
    }

    public async Task<OrganizationDetails> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(organizationId, false, cancellationToken);
        var organization = await database.Organizations.AsNoTracking().SingleAsync(item => item.Id == organizationId, cancellationToken);
        return Details(organization);
    }

    public async Task<OrganizationDetails> UpdateAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (request.Name is null && request.TimeZoneId is null)
        {
            throw Validation("organization_update_empty", "Provide a name or IANA time zone to update.");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var locked = await LockOwnedOrganizationAsync(organizationId, false, cancellationToken);
        try
        {
            locked.Organization.Update(request.Name ?? locked.Organization.Name,
                request.TimeZoneId ?? locked.Organization.TimeZoneId, timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (exception is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw Validation("organization_settings_invalid", "Provide a name of at most 160 characters and a valid IANA time zone.");
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Details(locked.Organization);
    }

    public async Task<IReadOnlyList<OrganizationMemberDto>> ListMembersAsync(Guid organizationId, int offset, int limit,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(organizationId, true, cancellationToken);
        ValidatePage(offset, limit);
        return await (from membership in database.OrganizationMemberships.AsNoTracking()
                      join user in database.Users.AsNoTracking() on membership.UserId equals user.Id
                      where membership.TenantId == organizationId
                      orderby user.Email, membership.Id
                      select new OrganizationMemberDto(membership.Id, membership.UserId, user.Email ?? string.Empty,
                          membership.Role, membership.IsActive, membership.CreatedAt)).Skip(offset).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<OrganizationMemberDto> ChangeRoleAsync(Guid organizationId, Guid membershipId,
        OrganizationRole role, CancellationToken cancellationToken)
    {
        ValidateRole(role);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockOwnedOrganizationAsync(organizationId, false, cancellationToken);
        var membership = await GetMembershipForMutationAsync(organizationId, membershipId, cancellationToken);
        if (membership.IsActive && membership.Role == OrganizationRole.OrganizationOwner && role != OrganizationRole.OrganizationOwner)
        {
            await RequireAnotherActiveOwnerAsync(organizationId, membership.Id, cancellationToken);
        }

        membership.ChangeRole(role, timeProvider.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await MemberDtoAsync(membership, cancellationToken);
    }

    public async Task<OrganizationMemberDto> SetMemberActiveAsync(Guid organizationId, Guid membershipId,
        bool isActive, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockOwnedOrganizationAsync(organizationId, false, cancellationToken);
        var membership = await GetMembershipForMutationAsync(organizationId, membershipId, cancellationToken);
        if (!isActive && membership.IsActive && membership.Role == OrganizationRole.OrganizationOwner)
        {
            await RequireAnotherActiveOwnerAsync(organizationId, membership.Id, cancellationToken);
        }

        if (isActive && !await database.Users.AsNoTracking().AnyAsync(user => user.Id == membership.UserId && user.IsActive, cancellationToken))
        {
            throw new FieldOpsException(ErrorKind.Conflict, "account_inactive", "An inactive account cannot have an active membership restored.");
        }

        membership.SetActive(isActive, timeProvider.GetUtcNow());
        if (!isActive)
        {
            var sessions = await database.AuthSessions.Where(session => session.UserId == membership.UserId
                && session.TenantId == organizationId && session.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.Revoke(timeProvider.GetUtcNow(), "membership_deactivated");
            }
        }

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await MemberDtoAsync(membership, cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationInvitationDto>> ListInvitationsAsync(Guid organizationId, int offset, int limit,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(organizationId, true, cancellationToken);
        ValidatePage(offset, limit);
        return await database.OrganizationInvitations.AsNoTracking().Where(invitation => invitation.TenantId == organizationId)
            .OrderByDescending(invitation => invitation.CreatedAt).ThenBy(invitation => invitation.Id).Skip(offset).Take(limit)
            .Select(invitation => new OrganizationInvitationDto(invitation.Id, invitation.NormalizedEmail, invitation.Role,
                invitation.CreatedAt, invitation.ExpiresAt, invitation.AcceptedAt, invitation.RevokedAt)).ToListAsync(cancellationToken);
    }

    private static void ValidatePage(int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 100)
            throw Validation("invalid_page", "Offset must be non-negative and limit must be between 1 and 100.");
    }

    public async Task<OrganizationInvitationDto> CreateInvitationAsync(Guid organizationId,
        CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        ValidateRole(request.Role);
        var email = NormalizeEmail(request.Email);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var locked = await LockOwnedOrganizationAsync(organizationId, true, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var existingMember = await (from membership in database.OrganizationMemberships.AsNoTracking()
                                    join user in database.Users.AsNoTracking() on membership.UserId equals user.Id
                                    where membership.TenantId == organizationId && user.NormalizedEmail == email
                                    select membership.Id).AnyAsync(cancellationToken);
        if (existingMember)
        {
            throw new FieldOpsException(ErrorKind.Conflict, "membership_exists", "This account already has a membership. Manage that membership instead.");
        }

        var pending = await database.OrganizationInvitations.Where(invitation => invitation.TenantId == organizationId
            && invitation.NormalizedEmail == email && invitation.AcceptedAt == null && invitation.RevokedAt == null).SingleOrDefaultAsync(cancellationToken);
        if (pending is not null)
        {
            await database.Entry(pending).ReloadAsync(cancellationToken);
            if (pending.ExpiresAt > now)
            {
                throw new FieldOpsException(ErrorKind.Conflict, "invitation_pending", "An invitation is already pending for this email.");
            }

            pending.Revoke(now);
            // Release the partial unique key before inserting its replacement.
            // Both saves remain inside the same transaction.
            await database.SaveChangesAsync(cancellationToken);
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var invitation = OrganizationInvitation.Create(organizationId, locked.Membership.Id, email, request.Role,
            HashToken(token), now.AddHours(72), now);
        database.OrganizationInvitations.Add(invitation);
        emailOutbox.Enqueue(new EmailMessage(request.Email.Trim(), "FieldOps organization invitation",
            $"You were invited to {locked.Organization.Name} as {request.Role}.\n" +
            $"Token: {token}\nExpires at: {invitation.ExpiresAt:O}\n" +
            "After signing in with this verified email, submit the token to POST /api/v1/invitations/accept.\n" +
            "If you did not expect this invitation, ignore this message."));
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InvitationDto(invitation);
    }

    public async Task RevokeInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockOwnedOrganizationAsync(organizationId, false, cancellationToken);
        var invitation = await database.OrganizationInvitations.SingleOrDefaultAsync(item => item.TenantId == organizationId
            && item.Id == invitationId, cancellationToken) ?? throw NotFound();
        await database.Entry(invitation).ReloadAsync(cancellationToken);
        if (invitation.AcceptedAt is not null)
        {
            throw new FieldOpsException(ErrorKind.Conflict, "invitation_consumed", "An accepted invitation cannot be revoked.");
        }

        invitation.Revoke(timeProvider.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<OrganizationSummary> AcceptInvitationAsync(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length != 64 || !request.Token.All(char.IsAsciiHexDigit))
        {
            throw InvalidInvitation();
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var user = await LockActorUserAsync(true, cancellationToken);
        var hash = HashToken(request.Token);
        // A hash of a high-entropy token and the authenticated, verified email are
        // the only bootstrap authority to discover the invitation's tenant.
        using var bootstrap = database.BeginSystemScope();
        var target = await database.OrganizationInvitations.AsNoTracking().Where(invitation => invitation.TokenHash == hash
            && invitation.NormalizedEmail == user.NormalizedEmail).Select(invitation => new { invitation.TenantId }).SingleOrDefaultAsync(cancellationToken)
            ?? throw InvalidInvitation();
        var organization = await LockOrganizationRowAsync(target.TenantId, cancellationToken);
        var invitation = await database.OrganizationInvitations.FromSqlInterpolated(
            $"SELECT * FROM \"OrganizationInvitations\" WHERE \"TenantId\" = {target.TenantId} AND \"TokenHash\" = {hash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw InvalidInvitation();
        await database.Entry(invitation).ReloadAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!invitation.CanAccept(now) || invitation.NormalizedEmail != user.NormalizedEmail)
        {
            throw InvalidInvitation();
        }

        var issuerStillAuthorized = await (from membership in database.OrganizationMemberships.AsNoTracking()
                                           join issuer in database.Users.AsNoTracking() on membership.UserId equals issuer.Id
                                           where membership.TenantId == target.TenantId && membership.Id == invitation.CreatedByMembershipId
                                               && membership.IsActive && membership.Role == OrganizationRole.OrganizationOwner && issuer.IsActive
                                           select membership.Id).AnyAsync(cancellationToken);
        if (!issuerStillAuthorized)
        {
            throw InvalidInvitation();
        }

        if (await database.OrganizationMemberships.AsNoTracking().AnyAsync(membership => membership.TenantId == target.TenantId
            && membership.UserId == user.Id, cancellationToken))
        {
            throw new FieldOpsException(ErrorKind.Conflict, "membership_exists", "This account already has a membership. Contact an organization owner.");
        }

        var acceptedMembership = OrganizationMembership.Create(organization.Id, user.Id, invitation.Role, now);
        database.OrganizationMemberships.Add(acceptedMembership);
        invitation.Accept(acceptedMembership.Id, now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OrganizationSummary(organization.Id, organization.Name, organization.TimeZoneId, acceptedMembership.Role);
    }

    private async Task<OrganizationMembership> RequireMembershipAsync(Guid organizationId, bool ownerRequired, CancellationToken cancellationToken)
    {
        var actor = RequireTenant(organizationId);
        await RequireActiveUserAsync(actor.UserId, false, cancellationToken);
        var membership = await database.OrganizationMemberships.AsNoTracking().SingleOrDefaultAsync(item => item.TenantId == organizationId
            && item.UserId == actor.UserId && item.IsActive, cancellationToken) ?? throw NotFound();
        if (!await database.Organizations.AsNoTracking().AnyAsync(item => item.Id == organizationId && item.IsActive, cancellationToken))
        {
            throw NotFound();
        }

        if (ownerRequired && membership.Role != OrganizationRole.OrganizationOwner)
        {
            throw Forbidden();
        }

        return membership;
    }

    private async Task<(Organization Organization, OrganizationMembership Membership)> LockOwnedOrganizationAsync(
        Guid organizationId, bool emailConfirmed, CancellationToken cancellationToken)
    {
        var actor = RequireTenant(organizationId);
        await LockActorUserAsync(emailConfirmed, cancellationToken);
        var organization = await LockOrganizationRowAsync(organizationId, cancellationToken);
        // Re-read after obtaining the organization lock: a competing owner may
        // have downgraded or deactivated the actor while this request waited.
        var membership = await database.OrganizationMemberships.AsNoTracking().SingleOrDefaultAsync(item => item.TenantId == organizationId
            && item.UserId == actor.UserId && item.IsActive, cancellationToken) ?? throw Forbidden();
        if (membership.Role != OrganizationRole.OrganizationOwner)
        {
            throw Forbidden();
        }

        return (organization, membership);
    }

    private async Task<Organization> LockOrganizationRowAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await database.Organizations.FromSqlInterpolated(
            $"SELECT * FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        await database.Entry(organization).ReloadAsync(cancellationToken);
        if (!organization.IsActive)
        {
            throw NotFound();
        }

        return organization;
    }

    private async Task<FieldOpsUser> LockActorUserAsync(bool emailConfirmed, CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var user = await database.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {actor.UserId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        ValidateUser(user, emailConfirmed);
        return user!;
    }

    private async Task RequireActiveUserAsync(Guid userId, bool emailConfirmed, CancellationToken cancellationToken)
    {
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        ValidateUser(user, emailConfirmed);
    }

    private static void ValidateUser(FieldOpsUser? user, bool emailConfirmed)
    {
        if (user is null || !user.IsActive)
        {
            throw new FieldOpsException(ErrorKind.Unauthenticated, "account_unavailable", "This account is unavailable.");
        }

        if (emailConfirmed && !user.EmailConfirmed)
        {
            throw new FieldOpsException(ErrorKind.Forbidden, "email_confirmation_required", "Confirm your email before this operation.");
        }
    }

    private async Task<OrganizationMembership> GetMembershipForMutationAsync(Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var membership = await database.OrganizationMemberships.SingleOrDefaultAsync(item => item.TenantId == organizationId
            && item.Id == membershipId, cancellationToken) ?? throw NotFound();
        await database.Entry(membership).ReloadAsync(cancellationToken);
        return membership;
    }

    private async Task RequireAnotherActiveOwnerAsync(Guid organizationId, Guid excludedMembershipId, CancellationToken cancellationToken)
    {
        var anotherOwner = await (from membership in database.OrganizationMemberships.AsNoTracking()
                                  join user in database.Users.AsNoTracking() on membership.UserId equals user.Id
                                  where membership.TenantId == organizationId && membership.Id != excludedMembershipId
                                      && membership.IsActive && membership.Role == OrganizationRole.OrganizationOwner && user.IsActive
                                  select membership.Id).AnyAsync(cancellationToken);
        if (!anotherOwner)
        {
            throw new FieldOpsException(ErrorKind.Conflict, "last_owner", "An organization must retain at least one active owner.");
        }
    }

    private async Task<OrganizationMemberDto> MemberDtoAsync(OrganizationMembership membership, CancellationToken cancellationToken)
    {
        var email = await database.Users.AsNoTracking().Where(user => user.Id == membership.UserId)
            .Select(user => user.Email ?? string.Empty).SingleAsync(cancellationToken);
        return new OrganizationMemberDto(membership.Id, membership.UserId, email, membership.Role, membership.IsActive, membership.CreatedAt);
    }

    private TenantActor RequireActor() => tenantContext.Actor
        ?? throw new FieldOpsException(ErrorKind.Unauthenticated, "authentication_required", "Authentication is required.");

    private TenantActor RequireTenant(Guid organizationId)
    {
        var actor = RequireActor();
        if (organizationId == Guid.Empty || actor.TenantId != organizationId)
        {
            throw NotFound();
        }

        return actor;
    }

    private static Organization CreateOrganization(string name, string timeZoneId, DateTimeOffset now)
    {
        try { return Organization.Create(name, timeZoneId, now); }
        catch (Exception exception) when (exception is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw Validation("organization_settings_invalid", "Provide a name of at most 160 characters and a valid IANA time zone.");
        }
    }

    private string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !MailAddress.TryCreate(email.Trim(), out var address)
            || !string.Equals(address.Address, email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw Validation("email_invalid", "Provide a valid email address of at most 254 characters.");
        }

        return normalizer.NormalizeEmail(email.Trim()) ?? throw Validation("email_invalid", "Provide a valid email address.");
    }

    private static void ValidateRole(OrganizationRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw Validation("role_invalid", "Provide a valid organization role.");
        }
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static OrganizationDetails Details(Organization organization) => new(organization.Id, organization.Name, organization.TimeZoneId, organization.CreatedAt, organization.UpdatedAt);
    private static OrganizationInvitationDto InvitationDto(OrganizationInvitation invitation) => new(invitation.Id, invitation.NormalizedEmail,
        invitation.Role, invitation.CreatedAt, invitation.ExpiresAt, invitation.AcceptedAt, invitation.RevokedAt);
    private static FieldOpsException NotFound() => new(ErrorKind.NotFound, "organization_resource_not_found", "The organization resource was not found.");
    private static FieldOpsException Forbidden() => new(ErrorKind.Forbidden, "organization_owner_required", "An active organization owner membership is required.");
    private static FieldOpsException Validation(string code, string message) => new(ErrorKind.Validation, code, message);
    private static FieldOpsException InvalidInvitation() => Validation("invitation_invalid", "The invitation is invalid or unavailable.");
}
