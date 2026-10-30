using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Finance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ManualJournalCorrectionV1 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "journal_corrections",
            schema: "finance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                original_journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                reversal_journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                replacement_journal_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                table.PrimaryKey("PK_journal_corrections", x => x.id);
                table.CheckConstraint("correction_valid", "original_journal_id<>reversal_journal_id AND original_journal_id<>replacement_journal_id AND reversal_journal_id<>replacement_journal_id AND length(btrim(reason)) BETWEEN 1 AND 1000");
                table.ForeignKey(
                    name: "FK_journal_corrections_journal_entries_tenant_id_company_id_or~",
                    columns: x => new { x.tenant_id, x.company_id, x.original_journal_id },
                    principalSchema: "finance",
                    principalTable: "journal_entries",
                    principalColumns: new[] { "tenant_id", "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_journal_corrections_journal_entries_tenant_id_company_id_re~",
                    columns: x => new { x.tenant_id, x.company_id, x.replacement_journal_id },
                    principalSchema: "finance",
                    principalTable: "journal_entries",
                    principalColumns: new[] { "tenant_id", "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_journal_corrections_journal_entries_tenant_id_company_id_r~1",
                    columns: x => new { x.tenant_id, x.company_id, x.reversal_journal_id },
                    principalSchema: "finance",
                    principalTable: "journal_entries",
                    principalColumns: new[] { "tenant_id", "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_journal_corrections_tenant_id_company_id_original_journal_id",
            schema: "finance",
            table: "journal_corrections",
            columns: new[] { "tenant_id", "company_id", "original_journal_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_journal_corrections_tenant_id_company_id_replacement_journa~",
            schema: "finance",
            table: "journal_corrections",
            columns: new[] { "tenant_id", "company_id", "replacement_journal_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_journal_corrections_tenant_id_company_id_reversal_journal_id",
            schema: "finance",
            table: "journal_corrections",
            columns: new[] { "tenant_id", "company_id", "reversal_journal_id" },
            unique: true);
        migrationBuilder.Sql(CorrectionSecuritySql.V1);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new InvalidOperationException("Correction relationships and posted financial history cannot be dropped by automatic downgrade. Use a reviewed forward migration.");
    }
}
