using FieldOps.Domain.Organizations;

namespace FieldOps.Application.Organizations;

public sealed record OrganizationSummary(Guid Id, string Name, string TimeZoneId, OrganizationRole Role);
public sealed record OrganizationDetails(Guid Id, string Name, string TimeZoneId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CreateOrganizationRequest(string Name, string TimeZoneId);
public sealed record UpdateOrganizationRequest(string? Name, string? TimeZoneId);
public sealed record ChangeMembershipRoleRequest(OrganizationRole Role);
public sealed record OrganizationMemberDto(Guid Id, Guid UserId, string Email, OrganizationRole Role, bool IsActive, DateTimeOffset CreatedAt);
public sealed record CreateInvitationRequest(string Email, OrganizationRole Role);
public sealed record AcceptInvitationRequest(string Token);
public sealed record OrganizationInvitationDto(Guid Id, string Email, OrganizationRole Role, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt, DateTimeOffset? RevokedAt);

public interface IOrganizationService
{
    Task<IReadOnlyList<OrganizationSummary>> ListAsync(CancellationToken cancellationToken);
    Task<OrganizationSummary> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken);
    Task<OrganizationDetails> GetAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<OrganizationDetails> UpdateAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationMemberDto>> ListMembersAsync(Guid organizationId, int offset, int limit, CancellationToken cancellationToken);
    Task<OrganizationMemberDto> ChangeRoleAsync(Guid organizationId, Guid membershipId, OrganizationRole role, CancellationToken cancellationToken);
    Task<OrganizationMemberDto> SetMemberActiveAsync(Guid organizationId, Guid membershipId, bool isActive, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrganizationInvitationDto>> ListInvitationsAsync(Guid organizationId, int offset, int limit, CancellationToken cancellationToken);
    Task<OrganizationInvitationDto> CreateInvitationAsync(Guid organizationId, CreateInvitationRequest request, CancellationToken cancellationToken);
    Task RevokeInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken);
    Task<OrganizationSummary> AcceptInvitationAsync(AcceptInvitationRequest request, CancellationToken cancellationToken);
}
