using FieldOps.Application.Common;
using FieldOps.Application.Tenancy;
using FieldOps.Domain.Identity;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Operations;
using FieldOps.Infrastructure.Email;
using FieldOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed partial class FieldOpsDbContext(DbContextOptions<FieldOpsDbContext> options,
    TenantContext? tenantContext = null) : IdentityUserContext<FieldOpsUser, Guid>(options)
{
    private int systemScopeDepth;
    internal bool IsSystemScope => systemScopeDepth > 0;
    internal Guid CurrentTenantId => tenantContext?.CurrentTenantId ?? Guid.Empty;
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshCredential> RefreshCredentials => Set<RefreshCredential>();
    public DbSet<OutboundEmail> OutboundEmails => Set<OutboundEmail>();

    internal IDisposable BeginSystemScope()
    {
        systemScopeDepth++;
        return new SystemScope(this);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var users = modelBuilder.Entity<FieldOpsUser>();
        users.Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
        users.Property(user => user.Email).HasMaxLength(254).IsRequired();
        users.Property(user => user.NormalizedEmail).HasMaxLength(254).IsRequired();
        users.HasIndex(user => user.NormalizedEmail).IsUnique();

        var sessions = modelBuilder.Entity<AuthSession>();
        sessions.ToTable("AuthSessions");
        sessions.HasKey(session => session.Id);
        sessions.Property(session => session.Transport).HasMaxLength(16).IsRequired();
        sessions.Property(session => session.SecurityStamp).HasMaxLength(256).IsRequired();
        sessions.Property(session => session.RevocationReason).HasMaxLength(64);
        sessions.HasIndex(session => new { session.UserId, session.TenantId });
        sessions.HasOne<AuthSession>().WithMany()
            .HasForeignKey(session => new { session.FamilyId, session.UserId })
            .HasPrincipalKey(session => new { session.Id, session.UserId }).OnDelete(DeleteBehavior.Restrict);
        sessions.HasOne<FieldOpsUser>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Restrict);
        sessions.HasOne<Organization>().WithMany().HasForeignKey(session => session.TenantId).OnDelete(DeleteBehavior.Restrict);
        sessions.HasOne<OrganizationMembership>().WithMany()
            .HasForeignKey(session => new { session.TenantId, session.UserId })
            .HasPrincipalKey(member => new { member.TenantId, member.UserId }).OnDelete(DeleteBehavior.Restrict);

        var refresh = modelBuilder.Entity<RefreshCredential>();
        refresh.ToTable("RefreshCredentials", table => table.HasCheckConstraint("CK_RefreshCredentials_Replacement",
            """("ConsumedAt" IS NULL) = ("ReplacedById" IS NULL) AND "Id" IS DISTINCT FROM "ReplacedById" """));
        refresh.HasKey(token => token.Id);
        refresh.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        refresh.HasIndex(token => token.TokenHash).IsUnique();
        refresh.HasIndex(token => token.ReplacedById).IsUnique();
        refresh.HasOne<AuthSession>().WithMany()
            .HasForeignKey(token => new { token.SessionId, token.FamilyId })
            .HasPrincipalKey(session => new { session.Id, session.FamilyId }).OnDelete(DeleteBehavior.Cascade);
        refresh.HasOne<RefreshCredential>().WithMany()
            .HasForeignKey(token => new { token.ReplacedById, token.FamilyId })
            .HasPrincipalKey(token => new { token.Id, token.FamilyId }).OnDelete(DeleteBehavior.NoAction);

        var mail = modelBuilder.Entity<OutboundEmail>();
        mail.ToTable("OutboundEmails");
        mail.HasKey(email => email.Id);
        mail.Property(email => email.ToEmail).HasMaxLength(254).IsRequired();
        mail.Property(email => email.Subject).HasMaxLength(200).IsRequired();
        mail.Property(email => email.ProtectedBody).IsRequired();
        mail.Property(email => email.LastFailureType).HasMaxLength(100);
        mail.HasIndex(email => new { email.IsDeadLetter, email.NextAttemptAt });
        ConfigureOrganizations(modelBuilder);
        ConfigureAudit(modelBuilder);
        ConfigureCatalog(modelBuilder);
        ConfigureWorkOrders(modelBuilder);
        ConfigureWorkOrderDetails(modelBuilder);
    }

    partial void ConfigureOrganizations(ModelBuilder modelBuilder);
    partial void ConfigureCatalog(ModelBuilder modelBuilder);
    partial void ConfigureWorkOrders(ModelBuilder modelBuilder);
    partial void ConfigureWorkOrderDetails(ModelBuilder modelBuilder);

    public override int SaveChanges() => SaveChanges(true);
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardTenantWritesAsync(false, default).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await GuardTenantWritesAsync(true, cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task GuardTenantWritesAsync(bool async, CancellationToken cancellationToken)
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<AuditLog>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new FieldOpsException(ErrorKind.Forbidden, "audit_immutable", "Recorded audit history cannot be changed.");
        if (IsSystemScope) return;
        foreach (var entry in ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity is not ITenantEntity && entry.Entity is not Organization) continue;
            if (CurrentTenantId == Guid.Empty) throw TenantBoundaryError();
            var tenantId = entry.Entity is Organization organization ? organization.Id : ((ITenantEntity)entry.Entity).TenantId;
            if (entry.State == EntityState.Added && entry.Entity is ITenantEntity && tenantId == Guid.Empty)
            {
                entry.Property(nameof(ITenantEntity.TenantId)).CurrentValue = CurrentTenantId;
                tenantId = CurrentTenantId;
            }
            if (tenantId != CurrentTenantId) throw TenantBoundaryError();
            if (entry.State == EntityState.Added) continue;
            var values = async ? await entry.GetDatabaseValuesAsync(cancellationToken) : entry.GetDatabaseValues();
            var property = entry.Entity is Organization ? nameof(Organization.Id) : nameof(ITenantEntity.TenantId);
            if (values is null || values.GetValue<Guid>(property) != CurrentTenantId) throw TenantBoundaryError();
            if (entry.Entity is ITenantEntity && entry.Property(nameof(ITenantEntity.TenantId)).OriginalValue is Guid original && original != tenantId)
                throw TenantBoundaryError();
        }
    }

    private static FieldOpsException TenantBoundaryError() => new(ErrorKind.Forbidden, "tenant_boundary", "This operation is outside the active organization.");

    private sealed class SystemScope(FieldOpsDbContext context) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            context.systemScopeDepth--;
        }
    }
}
