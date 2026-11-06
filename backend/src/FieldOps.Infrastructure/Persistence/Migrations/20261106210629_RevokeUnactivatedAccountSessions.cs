using Microsoft.EntityFrameworkCore.Migrations;

namespace FieldOps.Infrastructure.Persistence.Migrations;

public partial class RevokeUnactivatedAccountSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Revoking the session invalidates every refresh credential attached to it
        // while preserving credential hashes and their replacement history.
        migrationBuilder.Sql("""
            UPDATE "AuthSessions" AS session
            SET "RevokedAt" = CURRENT_TIMESTAMP,
                "RevocationReason" = 'account_activation_required'
            FROM "AspNetUsers" AS account
            WHERE account."Id" = session."UserId"
              AND session."RevokedAt" IS NULL
              AND session."ExpiresAt" > CURRENT_TIMESTAMP
              AND (NOT account."EmailConfirmed" OR COALESCE(account."PasswordHash", '') = '');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restoring sessions revoked before account activation is unsafe.");
}
