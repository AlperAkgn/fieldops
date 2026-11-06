using FieldOps.Domain.Organizations;
using FieldOps.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed partial class FieldOpsDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();

    partial void ConfigureOrganizations(ModelBuilder modelBuilder)
    {
        var organizations = modelBuilder.Entity<Organization>();
        organizations.ToTable("Organizations", table =>
            table.HasCheckConstraint("CK_Organizations_Name", "length(btrim(\"Name\")) BETWEEN 1 AND 160"));
        organizations.HasKey(organization => organization.Id);
        organizations.Property(organization => organization.Name).HasMaxLength(160).IsRequired();
        organizations.Property(organization => organization.TimeZoneId).HasMaxLength(100).IsRequired();
        organizations.HasQueryFilter(organization => IsSystemScope || organization.Id == CurrentTenantId);

        var memberships = modelBuilder.Entity<OrganizationMembership>();
        memberships.ToTable("OrganizationMemberships", table =>
            table.HasCheckConstraint("CK_OrganizationMemberships_Role", "\"Role\" IN ('OrganizationOwner', 'Manager', 'Technician', 'Viewer')"));
        memberships.HasKey(membership => membership.Id);
        memberships.HasAlternateKey(membership => new { membership.TenantId, membership.Id });
        memberships.HasAlternateKey(membership => new { membership.TenantId, membership.UserId });
        memberships.HasIndex(membership => membership.UserId);
        memberships.HasIndex(membership => new { membership.TenantId, membership.Role, membership.IsActive });
        memberships.Property(membership => membership.Role).HasConversion<string>().HasMaxLength(24);
        memberships.HasOne<Organization>().WithMany().HasForeignKey(membership => membership.TenantId).OnDelete(DeleteBehavior.Restrict);
        memberships.HasOne<FieldOpsUser>().WithMany().HasForeignKey(membership => membership.UserId).OnDelete(DeleteBehavior.Restrict);
        memberships.HasQueryFilter(membership => IsSystemScope || membership.TenantId == CurrentTenantId);

        var invitations = modelBuilder.Entity<OrganizationInvitation>();
        invitations.ToTable("OrganizationInvitations", table =>
        {
            table.HasCheckConstraint("CK_OrganizationInvitations_Role", "\"Role\" IN ('OrganizationOwner', 'Manager', 'Technician', 'Viewer')");
            table.HasCheckConstraint("CK_OrganizationInvitations_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
            table.HasCheckConstraint("CK_OrganizationInvitations_Hash", "\"TokenHash\" ~ '^[0-9A-F]{64}$'");
            table.HasCheckConstraint("CK_OrganizationInvitations_Acceptance", "(\"AcceptedAt\" IS NULL) = (\"AcceptedByMembershipId\" IS NULL)");
            table.HasCheckConstraint("CK_OrganizationInvitations_Consumption", "\"AcceptedAt\" IS NULL OR \"RevokedAt\" IS NULL");
        });
        invitations.HasKey(invitation => invitation.Id);
        invitations.HasAlternateKey(invitation => new { invitation.TenantId, invitation.Id });
        invitations.Property(invitation => invitation.NormalizedEmail).HasMaxLength(254).IsRequired();
        invitations.Property(invitation => invitation.Role).HasConversion<string>().HasMaxLength(24);
        invitations.Property(invitation => invitation.TokenHash).HasMaxLength(64).IsRequired();
        invitations.HasIndex(invitation => invitation.TokenHash).IsUnique();
        invitations.HasIndex(invitation => new { invitation.TenantId, invitation.NormalizedEmail })
            .IsUnique().HasFilter("\"AcceptedAt\" IS NULL AND \"RevokedAt\" IS NULL");
        invitations.HasOne<Organization>().WithMany().HasForeignKey(invitation => invitation.TenantId).OnDelete(DeleteBehavior.Restrict);
        invitations.HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey(invitation => new { invitation.TenantId, invitation.CreatedByMembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id }).OnDelete(DeleteBehavior.Restrict);
        invitations.HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey(invitation => new { invitation.TenantId, invitation.AcceptedByMembershipId })
            .HasPrincipalKey(membership => new { membership.TenantId, membership.Id }).OnDelete(DeleteBehavior.Restrict);
        invitations.HasQueryFilter(invitation => IsSystemScope || invitation.TenantId == CurrentTenantId);
    }
}
