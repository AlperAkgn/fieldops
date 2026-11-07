using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditOperationalChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "AuditLogs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_Metadata",
                table: "AuditLogs",
                sql: " \"Metadata\" IS NULL OR jsonb_typeof(\"Metadata\") = 'object' ");
            migrationBuilder.Sql("""
                CREATE FUNCTION public.fieldops_audit_history_immutable() RETURNS trigger
                LANGUAGE plpgsql AS $body$
                BEGIN
                    RAISE EXCEPTION 'Recorded audit history cannot be changed.' USING ERRCODE = '55000';
                END;
                $body$;
                CREATE TRIGGER audit_history_immutable
                BEFORE UPDATE OR DELETE OR TRUNCATE ON public."AuditLogs"
                FOR EACH STATEMENT EXECUTE FUNCTION public.fieldops_audit_history_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new System.NotSupportedException("Downgrading would remove recorded audit metadata and append-only protection.");
    }
}
