using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

// Phase 0 intentionally has no identity or operational entities.
public sealed class FieldOpsDbContext(DbContextOptions<FieldOpsDbContext> options) : DbContext(options);
