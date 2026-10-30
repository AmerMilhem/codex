using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Platform.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "audit");

        migrationBuilder.EnsureSchema(
            name: "integration");

        migrationBuilder.CreateTable(
            name: "entries",
            schema: "audit",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_issuer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                actor_subject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                entity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                before = table.Column<string>(type: "jsonb", nullable: true),
                after = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_entries", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "idempotency",
            schema: "integration",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                caller = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                command = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                target = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                reference = table.Column<Guid>(type: "uuid", nullable: true),
                applied_revision = table.Column<long>(type: "bigint", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_idempotency", x => new { x.tenant_id, x.company_id, x.caller, x.command, x.target, x.key });
                table.CheckConstraint("ck_idempotency_state", "state IN ('Processing','Completed') AND (state <> 'Completed' OR (reference IS NOT NULL AND applied_revision IS NOT NULL))");
            });

        migrationBuilder.CreateTable(
            name: "inbox",
            schema: "integration",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                consumer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox", x => new { x.tenant_id, x.company_id, x.consumer, x.event_id });
            });

        migrationBuilder.CreateTable(
            name: "outbox",
            schema: "integration",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                trace_parent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox", x => x.id);
                table.CheckConstraint("ck_outbox_state", "state IN ('Pending','Processing','Completed','Failed') AND attempts >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_entries_tenant_id_company_id_timestamp",
            schema: "audit",
            table: "entries",
            columns: new[] { "tenant_id", "company_id", "timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_tenant_id_company_id_state_available_at",
            schema: "integration",
            table: "outbox",
            columns: new[] { "tenant_id", "company_id", "state", "available_at" });
        migrationBuilder.Sql(SecuritySql.Version1);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "entries",
            schema: "audit");

        migrationBuilder.DropTable(
            name: "idempotency",
            schema: "integration");

        migrationBuilder.DropTable(
            name: "inbox",
            schema: "integration");

        migrationBuilder.DropTable(
            name: "outbox",
            schema: "integration");
    }
}
