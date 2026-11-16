using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CollaborationAndAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxNotifications", x => x.Id);
                    table.UniqueConstraint("AK_InboxNotifications_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_InboxNotifications_OrganizationMemberships_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InboxNotifications_WorkOrders_TenantId_WorkOrderId",
                        columns: x => new { x.TenantId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PushDevices",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedToken = table.Column<string>(type: "text", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDevices", x => new { x.TenantId, x.DeviceId, x.SessionId });
                    table.ForeignKey(
                        name: "FK_PushDevices_AuthSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AuthSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PushDevices_OrganizationMemberships_TenantId_UserId",
                        columns: x => new { x.TenantId, x.UserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploaderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Ready = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderAttachments_OrganizationMemberships_TenantId_Uploa~",
                        columns: x => new { x.TenantId, x.UploaderId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderAttachments_WorkOrders_TenantId_WorkOrderId",
                        columns: x => new { x.TenantId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FormerAssignees = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderEvents_WorkOrders_TenantId_WorkOrderId",
                        columns: x => new { x.TenantId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PushDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    TicketId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushDeliveries_InboxNotifications_TenantId_NotificationId",
                        columns: x => new { x.TenantId, x.NotificationId },
                        principalTable: "InboxNotifications",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PushDeliveries_PushDevices_TenantId_DeviceId_SessionId",
                        columns: x => new { x.TenantId, x.DeviceId, x.SessionId },
                        principalTable: "PushDevices",
                        principalColumns: new[] { "TenantId", "DeviceId", "SessionId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxNotifications_TenantId_UserId_CreatedAt",
                table: "InboxNotifications",
                columns: new[] { "TenantId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxNotifications_TenantId_UserId_WorkOrderId_Version_Kind",
                table: "InboxNotifications",
                columns: new[] { "TenantId", "UserId", "WorkOrderId", "Version", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxNotifications_TenantId_WorkOrderId",
                table: "InboxNotifications",
                columns: new[] { "TenantId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PushDeliveries_DueAt",
                table: "PushDeliveries",
                column: "DueAt",
                filter: "\"CompletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PushDeliveries_TenantId_DeviceId_SessionId",
                table: "PushDeliveries",
                columns: new[] { "TenantId", "DeviceId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PushDeliveries_TenantId_NotificationId_DeviceId_SessionId",
                table: "PushDeliveries",
                columns: new[] { "TenantId", "NotificationId", "DeviceId", "SessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_SessionId",
                table: "PushDevices",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_TenantId_UserId",
                table: "PushDevices",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_TokenHash",
                table: "PushDevices",
                column: "TokenHash",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAttachments_CreatedAt",
                table: "WorkOrderAttachments",
                column: "CreatedAt",
                filter: "\"Ready\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAttachments_TenantId_UploaderId",
                table: "WorkOrderAttachments",
                columns: new[] { "TenantId", "UploaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAttachments_TenantId_WorkOrderId_CreatedAt",
                table: "WorkOrderAttachments",
                columns: new[] { "TenantId", "WorkOrderId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAttachments_TenantId_WorkOrderId_UploaderId_Operat~",
                table: "WorkOrderAttachments",
                columns: new[] { "TenantId", "WorkOrderId", "UploaderId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderEvents_CreatedAt_Id",
                table: "WorkOrderEvents",
                columns: new[] { "CreatedAt", "Id" },
                filter: "\"DispatchedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderEvents_TenantId_WorkOrderId_Version",
                table: "WorkOrderEvents",
                columns: new[] { "TenantId", "WorkOrderId", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "WorkOrderAttachments", "InboxNotifications", "PushDevices" IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "WorkOrderAttachments") OR EXISTS (SELECT 1 FROM "InboxNotifications")
                        OR EXISTS (SELECT 1 FROM "PushDevices") THEN
                        RAISE EXCEPTION 'Phase 6 attachment and notification data must be preserved.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "PushDeliveries");

            migrationBuilder.DropTable(
                name: "WorkOrderAttachments");

            migrationBuilder.DropTable(
                name: "WorkOrderEvents");

            migrationBuilder.DropTable(
                name: "InboxNotifications");

            migrationBuilder.DropTable(
                name: "PushDevices");
        }
    }
}
