using FieldOps.Api.Security;
using FieldOps.Application.Identity;
using FieldOps.Application.Organizations;

namespace FieldOps.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this WebApplication app)
    {
        var organizations = app.MapGroup("/api/v1/organizations").WithTags("Organizations")
            .RequireAuthorization("OrganizationRead").RequireRateLimiting("api").WithProblemResponses(401, 429);
        organizations.MapGet("/", async (IOrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct)));
        organizations.MapPost("/", async (CreateOrganizationRequest request, IOrganizationService service, CancellationToken ct) =>
        {
            var organization = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/v1/organizations/{organization.Id}", organization);
        }).WithProblemResponses(400, 403);
        organizations.MapGet("/{id:guid}", async (Guid id, IOrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(id, ct))).WithProblemResponses(404);
        organizations.MapPatch("/{id:guid}", async (Guid id, UpdateOrganizationRequest request, IOrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.UpdateAsync(id, request, ct))).RequireAuthorization("OrganizationOwner").WithProblemResponses(400, 403, 404);
        organizations.MapPost("/{id:guid}/select", async (Guid id, RefreshRequest request, HttpContext http,
            IIdentityService service, CancellationToken ct) =>
        {
            ApiSecurity.NativeRequest(http);
            return TypedResults.Ok(await service.SelectOrganizationAsync(ApiSecurity.SelectionSession(http), id, SessionTransport.Native,
                request.RefreshToken, ct));
        }).WithMetadata(new OrganizationSelectionAuthentication()).WithProblemResponses(400, 404);
        organizations.MapGet("/{id:guid}/members", async (Guid id, int? offset, int? limit, IOrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListMembersAsync(id, offset ?? 0, limit ?? 50, ct)))
            .RequireAuthorization("OrganizationOwner").WithProblemResponses(400, 403, 404);
        organizations.MapPatch("/{id:guid}/members/{membershipId:guid}/role", async (Guid id, Guid membershipId,
            ChangeMembershipRoleRequest request, IOrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ChangeRoleAsync(id, membershipId, request.Role, ct)))
            .RequireAuthorization("OrganizationOwner").WithProblemResponses(400, 403, 404, 409);
        organizations.MapPost("/{id:guid}/members/{membershipId:guid}/deactivate", async (Guid id, Guid membershipId,
            IOrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.SetMemberActiveAsync(id, membershipId, false, ct)))
            .RequireAuthorization("OrganizationOwner").WithProblemResponses(403, 404, 409);
        organizations.MapPost("/{id:guid}/members/{membershipId:guid}/reactivate", async (Guid id, Guid membershipId,
            IOrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.SetMemberActiveAsync(id, membershipId, true, ct)))
            .RequireAuthorization("OrganizationOwner").WithProblemResponses(403, 404, 409);
        organizations.MapGet("/{id:guid}/invitations", async (Guid id, int? offset, int? limit, IOrganizationService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListInvitationsAsync(id, offset ?? 0, limit ?? 50, ct)))
            .RequireAuthorization("OrganizationOwner").WithProblemResponses(400, 403, 404);
        organizations.MapPost("/{id:guid}/invitations", async (Guid id, CreateInvitationRequest request,
            IOrganizationService service, CancellationToken ct) =>
            TypedResults.Created((string?)null, await service.CreateInvitationAsync(id, request, ct)))
            .RequireAuthorization("OrganizationOwner").WithProblemResponses(400, 403, 404, 409);
        organizations.MapDelete("/{id:guid}/invitations/{invitationId:guid}", async (Guid id, Guid invitationId,
            IOrganizationService service, CancellationToken ct) =>
        {
            await service.RevokeInvitationAsync(id, invitationId, ct);
            return TypedResults.NoContent();
        }).RequireAuthorization("OrganizationOwner").WithProblemResponses(403, 404, 409);
        app.MapPost("/api/v1/invitations/accept", async (AcceptInvitationRequest request,
            IOrganizationService service, CancellationToken ct) => TypedResults.Ok(await service.AcceptInvitationAsync(request, ct)))
            .WithTags("Organizations").RequireAuthorization().RequireRateLimiting("api").WithProblemResponses(400, 401, 403, 409, 429);
    }
}
