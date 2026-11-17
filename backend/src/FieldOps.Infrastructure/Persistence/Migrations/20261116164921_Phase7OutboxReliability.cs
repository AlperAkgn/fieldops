using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7OutboxReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrderEvents_CreatedAt_Id",
                table: "WorkOrderEvents");

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "WorkOrderEvents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FailedAt",
                table: "WorkOrderEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                table: "WorkOrderEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "WorkOrderEvents",
                type: "character varying(55)",
                maxLength: 55,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderEvents_CreatedAt_Id",
                table: "WorkOrderEvents",
                columns: new[] { "CreatedAt", "Id" },
                filter: "\"DispatchedAt\" IS NULL AND \"FailedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "WorkOrderEvents" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "WorkOrderEvents"
                        WHERE "Attempts" <> 0 OR "NextAttemptAt" IS NOT NULL OR "FailedAt" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot downgrade Phase 7 while persisted outbox delivery state exists. Restore a verified backup instead.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropIndex(
                name: "IX_WorkOrderEvents_CreatedAt_Id",
                table: "WorkOrderEvents");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "WorkOrderEvents");

            migrationBuilder.DropColumn(
                name: "FailedAt",
                table: "WorkOrderEvents");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "WorkOrderEvents");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "WorkOrderEvents");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderEvents_CreatedAt_Id",
                table: "WorkOrderEvents",
                columns: new[] { "CreatedAt", "Id" },
                filter: "\"DispatchedAt\" IS NULL");
        }
    }
}
