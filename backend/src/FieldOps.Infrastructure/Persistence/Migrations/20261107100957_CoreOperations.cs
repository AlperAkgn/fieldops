using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoreOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCategories", x => x.Id);
                    table.UniqueConstraint("AK_AssetCategories_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_AssetCategories_Name", "length(btrim(\"Name\")) > 0 AND length(btrim(\"NormalizedName\")) > 0");
                    table.CheckConstraint("CK_AssetCategories_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_AssetCategories_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.UniqueConstraint("AK_AuditLogs_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_AuditLogs_OrganizationMemberships_TenantId_ActorUserId",
                        columns: x => new { x.TenantId, x.ActorUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChecklistTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistTemplates", x => x.Id);
                    table.UniqueConstraint("AK_ChecklistTemplates_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_ChecklistTemplates_Name", "length(btrim(\"Name\")) BETWEEN 1 AND 120");
                    table.CheckConstraint("CK_ChecklistTemplates_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ChecklistTemplates_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.UniqueConstraint("AK_Customers_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Customers_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Customers_Type", "\"CustomerType\" IN ('Individual', 'Business')");
                    table.CheckConstraint("CK_Customers_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_Customers_Organizations_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChecklistTemplateItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChecklistTemplateItems", x => x.Id);
                    table.UniqueConstraint("AK_ChecklistTemplateItems_TenantId_TemplateId_Id", x => new { x.TenantId, x.TemplateId, x.Id });
                    table.CheckConstraint("CK_ChecklistTemplateItems_Label", "length(btrim(\"Label\")) BETWEEN 1 AND 240");
                    table.CheckConstraint("CK_ChecklistTemplateItems_Position", "\"Position\" BETWEEN 0 AND 999");
                    table.ForeignKey(
                        name: "FK_ChecklistTemplateItems_ChecklistTemplates_TenantId_Template~",
                        columns: x => new { x.TenantId, x.TemplateId },
                        principalTable: "ChecklistTemplates",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AddressLine = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    StateOrProvince = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    Latitude = table.Column<decimal>(type: "numeric", nullable: true),
                    Longitude = table.Column<decimal>(type: "numeric", nullable: true),
                    ContactName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLocations", x => x.Id);
                    table.UniqueConstraint("AK_ServiceLocations_TenantId_CustomerId_Id", x => new { x.TenantId, x.CustomerId, x.Id });
                    table.UniqueConstraint("AK_ServiceLocations_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_ServiceLocations_Coordinates", "(\"Latitude\" IS NULL) = (\"Longitude\" IS NULL) AND \"Latitude\" BETWEEN -90 AND 90 AND \"Longitude\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_ServiceLocations_Fields", "length(btrim(\"Name\")) > 0 AND length(btrim(\"AddressLine\")) > 0 AND length(btrim(\"City\")) > 0 AND length(btrim(\"Country\")) > 0");
                    table.CheckConstraint("CK_ServiceLocations_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ServiceLocations_Customers_TenantId_CustomerId",
                        columns: x => new { x.TenantId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssetCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedAssetCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    InstallationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    WarrantyExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.UniqueConstraint("AK_Assets_TenantId_CustomerId_ServiceLocationId_Id", x => new { x.TenantId, x.CustomerId, x.ServiceLocationId, x.Id });
                    table.UniqueConstraint("AK_Assets_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Assets_Dates", "\"WarrantyExpirationDate\" >= \"InstallationDate\"");
                    table.CheckConstraint("CK_Assets_Fields", "length(btrim(\"AssetCode\")) > 0 AND length(btrim(\"NormalizedAssetCode\")) > 0 AND length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Assets_Status", "\"Status\" IN ('Active', 'UnderMaintenance', 'OutOfService', 'Retired')");
                    table.CheckConstraint("CK_Assets_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_Assets_AssetCategories_TenantId_CategoryId",
                        columns: x => new { x.TenantId, x.CategoryId },
                        principalTable: "AssetCategories",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assets_ServiceLocations_TenantId_CustomerId_ServiceLocation~",
                        columns: x => new { x.TenantId, x.CustomerId, x.ServiceLocationId },
                        principalTable: "ServiceLocations",
                        principalColumns: new[] { "TenantId", "CustomerId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderNumber = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Priority = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PlannedStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlannedEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.UniqueConstraint("AK_WorkOrders_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_WorkOrders_ActualInterval", "(\"ActualStartAt\" IS NULL AND \"ActualEndAt\" IS NULL AND \"Status\" IN ('Draft', 'Scheduled', 'Assigned', 'Cancelled'))\nOR (\"ActualStartAt\" IS NOT NULL AND \"ActualEndAt\" IS NULL AND \"Status\" IN ('InProgress', 'OnHold'))\nOR (\"ActualStartAt\" IS NOT NULL AND \"ActualEndAt\" IS NOT NULL AND \"ActualStartAt\" <= \"ActualEndAt\" AND \"Status\" IN ('Completed', 'Cancelled'))");
                    table.CheckConstraint("CK_WorkOrders_PlannedInterval", "(\"PlannedStartAt\" IS NULL AND \"PlannedEndAt\" IS NULL AND \"Status\" IN ('Draft', 'Cancelled'))\nOR (\"PlannedStartAt\" IS NOT NULL AND \"PlannedEndAt\" IS NOT NULL AND \"PlannedStartAt\" < \"PlannedEndAt\" AND \"Status\" <> 'Draft')");
                    table.CheckConstraint("CK_WorkOrders_Priority", " \"Priority\" IN ('Low', 'Medium', 'High', 'Critical') ");
                    table.CheckConstraint("CK_WorkOrders_Status", " \"Status\" IN ('Draft', 'Scheduled', 'Assigned', 'InProgress', 'OnHold', 'Completed', 'Cancelled') ");
                    table.CheckConstraint("CK_WorkOrders_Version", " \"Version\" > 0 ");
                    table.ForeignKey(
                        name: "FK_WorkOrders_Assets_TenantId_CustomerId_ServiceLocationId_Ass~",
                        columns: x => new { x.TenantId, x.CustomerId, x.ServiceLocationId, x.AssetId },
                        principalTable: "Assets",
                        principalColumns: new[] { "TenantId", "CustomerId", "ServiceLocationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Customers_TenantId_CustomerId",
                        columns: x => new { x.TenantId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_OrganizationMemberships_TenantId_CreatedByUserId",
                        columns: x => new { x.TenantId, x.CreatedByUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_ServiceLocations_TenantId_CustomerId_ServiceLoca~",
                        columns: x => new { x.TenantId, x.CustomerId, x.ServiceLocationId },
                        principalTable: "ServiceLocations",
                        principalColumns: new[] { "TenantId", "CustomerId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicianUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderAssignments", x => x.Id);
                    table.CheckConstraint("CK_WorkOrderAssignments_RemovedAt", " \"RemovedAt\" IS NULL OR \"AssignedAt\" <= \"RemovedAt\" ");
                    table.ForeignKey(
                        name: "FK_WorkOrderAssignments_OrganizationMemberships_TenantId_Assig~",
                        columns: x => new { x.TenantId, x.AssignedByUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderAssignments_OrganizationMemberships_TenantId_Techn~",
                        columns: x => new { x.TenantId, x.TechnicianUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderAssignments_WorkOrders_TenantId_WorkOrderId",
                        columns: x => new { x.TenantId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderChecklistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTemplateVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourceTemplateItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderChecklistItems", x => x.Id);
                    table.CheckConstraint("CK_WorkOrderChecklistItems_Completion", "(\"IsCompleted\" AND \"CompletedByUserId\" IS NOT NULL AND \"CompletedAt\" IS NOT NULL) OR\n(NOT \"IsCompleted\" AND \"CompletedByUserId\" IS NULL AND \"CompletedAt\" IS NULL)");
                    table.CheckConstraint("CK_WorkOrderChecklistItems_Label", "length(btrim(\"Label\")) BETWEEN 1 AND 240");
                    table.CheckConstraint("CK_WorkOrderChecklistItems_Position", "\"Position\" BETWEEN 0 AND 999");
                    table.CheckConstraint("CK_WorkOrderChecklistItems_SourceVersion", "\"SourceTemplateVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_WorkOrderChecklistItems_ChecklistTemplateItems_TenantId_Sou~",
                        columns: x => new { x.TenantId, x.SourceTemplateId, x.SourceTemplateItemId },
                        principalTable: "ChecklistTemplateItems",
                        principalColumns: new[] { "TenantId", "TemplateId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderChecklistItems_OrganizationMemberships_TenantId_Co~",
                        columns: x => new { x.TenantId, x.CompletedByUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderChecklistItems_WorkOrders_TenantId_WorkOrderId",
                        columns: x => new { x.TenantId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Visibility = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderComments", x => x.Id);
                    table.CheckConstraint("CK_WorkOrderComments_Body", "length(btrim(\"Body\")) BETWEEN 1 AND 4000");
                    table.CheckConstraint("CK_WorkOrderComments_Visibility", "\"Visibility\" IN ('TechnicianVisible', 'Internal')");
                    table.ForeignKey(
                        name: "FK_WorkOrderComments_OrganizationMemberships_TenantId_AuthorUs~",
                        columns: x => new { x.TenantId, x.AuthorUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderComments_WorkOrders_TenantId_WorkOrderId",
                        columns: x => new { x.TenantId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WorkOrderVersion = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderStatusHistories_OrganizationMemberships_TenantId_C~",
                        columns: x => new { x.TenantId, x.ChangedByUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "TenantId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderStatusHistories_WorkOrders_TenantId_WorkOrderId",
                        columns: x => new { x.TenantId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_TenantId_NormalizedName",
                table: "AssetCategories",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_CategoryId",
                table: "Assets",
                columns: new[] { "TenantId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_IsActive_Name_Id",
                table: "Assets",
                columns: new[] { "TenantId", "IsActive", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_NormalizedAssetCode",
                table: "Assets",
                columns: new[] { "TenantId", "NormalizedAssetCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_ActorUserId",
                table: "AuditLogs",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_OccurredAt_Id",
                table: "AuditLogs",
                columns: new[] { "TenantId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_ResourceType_ResourceId_OccurredAt",
                table: "AuditLogs",
                columns: new[] { "TenantId", "ResourceType", "ResourceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTemplateItems_TenantId_TemplateId_IsActive_Positio~",
                table: "ChecklistTemplateItems",
                columns: new[] { "TenantId", "TemplateId", "IsActive", "Position", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistTemplates_TenantId_IsActive_Name_Id",
                table: "ChecklistTemplates",
                columns: new[] { "TenantId", "IsActive", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_IsActive_Name_Id",
                table: "Customers",
                columns: new[] { "TenantId", "IsActive", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLocations_TenantId_IsActive_Name_Id",
                table: "ServiceLocations",
                columns: new[] { "TenantId", "IsActive", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAssignments_TenantId_AssignedByUserId",
                table: "WorkOrderAssignments",
                columns: new[] { "TenantId", "AssignedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAssignments_TenantId_TechnicianUserId_WorkOrderId",
                table: "WorkOrderAssignments",
                columns: new[] { "TenantId", "TechnicianUserId", "WorkOrderId" },
                filter: " \"RemovedAt\" IS NULL ");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAssignments_TenantId_WorkOrderId",
                table: "WorkOrderAssignments",
                columns: new[] { "TenantId", "WorkOrderId" },
                unique: true,
                filter: " \"IsPrimary\" AND \"RemovedAt\" IS NULL ");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAssignments_TenantId_WorkOrderId_TechnicianUserId",
                table: "WorkOrderAssignments",
                columns: new[] { "TenantId", "WorkOrderId", "TechnicianUserId" },
                unique: true,
                filter: " \"RemovedAt\" IS NULL ");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderChecklistItems_TenantId_CompletedByUserId",
                table: "WorkOrderChecklistItems",
                columns: new[] { "TenantId", "CompletedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderChecklistItems_TenantId_SourceTemplateId_SourceTem~",
                table: "WorkOrderChecklistItems",
                columns: new[] { "TenantId", "SourceTemplateId", "SourceTemplateItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderChecklistItems_TenantId_WorkOrderId_Position_Id",
                table: "WorkOrderChecklistItems",
                columns: new[] { "TenantId", "WorkOrderId", "Position", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderChecklistItems_TenantId_WorkOrderId_SourceTemplate~",
                table: "WorkOrderChecklistItems",
                columns: new[] { "TenantId", "WorkOrderId", "SourceTemplateItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderComments_TenantId_AuthorUserId",
                table: "WorkOrderComments",
                columns: new[] { "TenantId", "AuthorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderComments_TenantId_WorkOrderId_CreatedAt_Id",
                table: "WorkOrderComments",
                columns: new[] { "TenantId", "WorkOrderId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_AssetId_ActualEndAt",
                table: "WorkOrders",
                columns: new[] { "TenantId", "AssetId", "ActualEndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_CreatedAt_Id",
                table: "WorkOrders",
                columns: new[] { "TenantId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_CreatedByUserId",
                table: "WorkOrders",
                columns: new[] { "TenantId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_CustomerId_ServiceLocationId_AssetId",
                table: "WorkOrders",
                columns: new[] { "TenantId", "CustomerId", "ServiceLocationId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_Status_PlannedStartAt",
                table: "WorkOrders",
                columns: new[] { "TenantId", "Status", "PlannedStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_TenantId_WorkOrderNumber",
                table: "WorkOrders",
                columns: new[] { "TenantId", "WorkOrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderStatusHistories_TenantId_ChangedByUserId",
                table: "WorkOrderStatusHistories",
                columns: new[] { "TenantId", "ChangedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderStatusHistories_TenantId_WorkOrderId_WorkOrderVers~",
                table: "WorkOrderStatusHistories",
                columns: new[] { "TenantId", "WorkOrderId", "WorkOrderVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Downgrading would delete operational and audit history. Use a reviewed forward migration or a verified backup.");
    }
}
