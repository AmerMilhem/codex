using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Finance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ManualJournalReversalV1 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "journal_reversals",
            schema: "finance",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                original_journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                reversal_journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                actor_issuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                actor_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_journal_reversals", x => new { x.tenant_id, x.company_id, x.original_journal_id });
                table.CheckConstraint("reversal_valid", "original_journal_id<>reversal_journal_id AND length(btrim(reason)) BETWEEN 1 AND 1000");
                table.ForeignKey(
                    name: "FK_journal_reversals_journal_entries_tenant_id_company_id_orig~",
                    columns: x => new { x.tenant_id, x.company_id, x.original_journal_id },
                    principalSchema: "finance",
                    principalTable: "journal_entries",
                    principalColumns: new[] { "tenant_id", "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_journal_reversals_journal_entries_tenant_id_company_id_reve~",
                    columns: x => new { x.tenant_id, x.company_id, x.reversal_journal_id },
                    principalSchema: "finance",
                    principalTable: "journal_entries",
                    principalColumns: new[] { "tenant_id", "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_journal_reversals_tenant_id_company_id_reversal_journal_id",
            schema: "finance",
            table: "journal_reversals",
            columns: new[] { "tenant_id", "company_id", "reversal_journal_id" },
            unique: true);
        migrationBuilder.Sql(ReversalSecuritySql.V1);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION finance.require_reversal_integrity() CASCADE; DROP FUNCTION finance.guard_reversal() CASCADE;");
        migrationBuilder.DropTable(
            name: "journal_reversals",
            schema: "finance");
    }
}
