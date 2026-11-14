using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfflineSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncReceipts",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ClientCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncReceipts", x => new { x.TenantId, x.UserId, x.OperationId });
                    table.ForeignKey(
                        name: "FK_SyncReceipts_OrganizationMemberships_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncReceipts_WorkOrders_TenantId_WorkOrderId",
                        columns: x => new { x.TenantId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncWatermarks",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncWatermarks", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_SyncWatermarks_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncWorkOrderChanges",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncWorkOrderChanges", x => new { x.TenantId, x.WorkOrderId });
                    table.ForeignKey(
                        name: "FK_SyncWorkOrderChanges_WorkOrders_TenantId_WorkOrderId",
                        columns: x => new { x.TenantId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncReceipts_TenantId_WorkOrderId",
                table: "SyncReceipts",
                columns: new[] { "TenantId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncWorkOrderChanges_TenantId_Position",
                table: "SyncWorkOrderChanges",
                columns: new[] { "TenantId", "Position" },
                unique: true);

            migrationBuilder.Sql("""
                LOCK TABLE "WorkOrders" IN SHARE ROW EXCLUSIVE MODE;

                INSERT INTO "SyncWorkOrderChanges" ("TenantId", "WorkOrderId", "Position")
                SELECT "TenantId", "Id", row_number() OVER (PARTITION BY "TenantId" ORDER BY "Id")
                FROM "WorkOrders";

                INSERT INTO "SyncWatermarks" ("TenantId", "Position")
                SELECT "TenantId", max("Position") FROM "SyncWorkOrderChanges" GROUP BY "TenantId";

                CREATE FUNCTION fieldops_track_work_order_change() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE next_position bigint;
                BEGIN
                    INSERT INTO "SyncWatermarks" AS watermark ("TenantId", "Position")
                    VALUES (NEW."TenantId", 1)
                    ON CONFLICT ("TenantId") DO UPDATE SET "Position" = watermark."Position" + 1
                    RETURNING "Position" INTO next_position;

                    INSERT INTO "SyncWorkOrderChanges" ("TenantId", "WorkOrderId", "Position")
                    VALUES (NEW."TenantId", NEW."Id", next_position)
                    ON CONFLICT ("TenantId", "WorkOrderId") DO UPDATE SET "Position" = EXCLUDED."Position";
                    RETURN NEW;
                END
                $$;

                CREATE TRIGGER fieldops_track_work_order_change
                AFTER INSERT OR UPDATE ON "WorkOrders"
                FOR EACH ROW EXECUTE FUNCTION fieldops_track_work_order_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "WorkOrders", "SyncReceipts" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "SyncReceipts") THEN
                        RAISE EXCEPTION 'Offline synchronization receipts must be preserved for safe replay.';
                    END IF;
                END $$;
                DROP TRIGGER fieldops_track_work_order_change ON "WorkOrders";
                DROP FUNCTION fieldops_track_work_order_change();
                """);
            migrationBuilder.DropTable(
                name: "SyncReceipts");

            migrationBuilder.DropTable(
                name: "SyncWatermarks");

            migrationBuilder.DropTable(
                name: "SyncWorkOrderChanges");
        }
    }
}
