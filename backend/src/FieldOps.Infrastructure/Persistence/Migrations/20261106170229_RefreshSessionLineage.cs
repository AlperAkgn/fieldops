using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefreshSessionLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefreshCredentials_AuthSessions_SessionId",
                table: "RefreshCredentials");

            migrationBuilder.DropIndex(
                name: "IX_RefreshCredentials_SessionId",
                table: "RefreshCredentials");

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "RefreshCredentials",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "AuthSessions",
                type: "uuid",
                nullable: true);

            // Older switches had no successor link. Revoke active sessions only for accounts
            // whose historical switch may have left an untraceable successor.
            migrationBuilder.Sql("""
                UPDATE "AuthSessions" SET "FamilyId" = "Id";
                UPDATE "RefreshCredentials" AS credential SET "FamilyId" = session."FamilyId"
                FROM "AuthSessions" AS session WHERE session."Id" = credential."SessionId";
                UPDATE "AuthSessions" AS active
                SET "RevokedAt" = CURRENT_TIMESTAMP, "RevocationReason" = 'legacy_switch_reauthentication'
                WHERE active."RevokedAt" IS NULL AND active."ExpiresAt" > CURRENT_TIMESTAMP
                  AND EXISTS (SELECT 1 FROM "AuthSessions" AS previous
                              WHERE previous."UserId" = active."UserId" AND previous."RevocationReason" = 'organization_changed');
                """);

            migrationBuilder.AlterColumn<Guid>(name: "FamilyId", table: "AuthSessions", type: "uuid",
                nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.AlterColumn<Guid>(name: "FamilyId", table: "RefreshCredentials", type: "uuid",
                nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_RefreshCredentials_Id_FamilyId",
                table: "RefreshCredentials",
                columns: new[] { "Id", "FamilyId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AuthSessions_Id_FamilyId",
                table: "AuthSessions",
                columns: new[] { "Id", "FamilyId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AuthSessions_Id_UserId",
                table: "AuthSessions",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshCredentials_ReplacedById",
                table: "RefreshCredentials",
                column: "ReplacedById",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshCredentials_ReplacedById_FamilyId",
                table: "RefreshCredentials",
                columns: new[] { "ReplacedById", "FamilyId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshCredentials_SessionId_FamilyId",
                table: "RefreshCredentials",
                columns: new[] { "SessionId", "FamilyId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RefreshCredentials_Replacement",
                table: "RefreshCredentials",
                sql: "(\"ConsumedAt\" IS NULL) = (\"ReplacedById\" IS NULL) AND \"Id\" IS DISTINCT FROM \"ReplacedById\" ");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_FamilyId_UserId",
                table: "AuthSessions",
                columns: new[] { "FamilyId", "UserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AuthSessions_AuthSessions_FamilyId_UserId",
                table: "AuthSessions",
                columns: new[] { "FamilyId", "UserId" },
                principalTable: "AuthSessions",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshCredentials_AuthSessions_SessionId_FamilyId",
                table: "RefreshCredentials",
                columns: new[] { "SessionId", "FamilyId" },
                principalTable: "AuthSessions",
                principalColumns: new[] { "Id", "FamilyId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshCredentials_RefreshCredentials_ReplacedById_FamilyId",
                table: "RefreshCredentials",
                columns: new[] { "ReplacedById", "FamilyId" },
                principalTable: "RefreshCredentials",
                principalColumns: new[] { "Id", "FamilyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Downgrading would remove refresh replay protection across organization switches.");
    }
}
