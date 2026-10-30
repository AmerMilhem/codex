using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Finance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class ManualJournalV1 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "finance");

        migrationBuilder.CreateTable(
            name: "accounting_books",
            schema: "finance",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                minor_units = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_accounting_books", x => new { x.tenant_id, x.company_id });
                table.CheckConstraint("book_currency_policy", "currency ~ '^[A-Z]{3}$' AND minor_units BETWEEN 0 AND 6");
            });

        migrationBuilder.CreateTable(
            name: "accounts",
            schema: "finance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                posting_allowed = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_accounts", x => x.id);
                table.UniqueConstraint("AK_accounts_tenant_id_company_id_id", x => new { x.tenant_id, x.company_id, x.id });
                table.CheckConstraint("account_valid", "length(code)>0 AND length(name)>0 AND status IN ('Active','Inactive')");
                table.ForeignKey(
                    name: "FK_accounts_accounting_books_tenant_id_company_id",
                    columns: x => new { x.tenant_id, x.company_id },
                    principalSchema: "finance",
                    principalTable: "accounting_books",
                    principalColumns: new[] { "tenant_id", "company_id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "fiscal_periods",
            schema: "finance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                fiscal_year = table.Column<int>(type: "integer", nullable: false),
                starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_fiscal_periods", x => x.id);
                table.UniqueConstraint("AK_fiscal_periods_tenant_id_company_id_id", x => new { x.tenant_id, x.company_id, x.id });
                table.CheckConstraint("period_valid", "starts_on<=ends_on AND fiscal_year BETWEEN 1 AND 9999 AND status IN ('Open','Closed')");
                table.ForeignKey(
                    name: "FK_fiscal_periods_accounting_books_tenant_id_company_id",
                    columns: x => new { x.tenant_id, x.company_id },
                    principalSchema: "finance",
                    principalTable: "accounting_books",
                    principalColumns: new[] { "tenant_id", "company_id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "journal_numbers",
            schema: "finance",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                fiscal_year = table.Column<int>(type: "integer", nullable: false),
                last_number = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_journal_numbers", x => new { x.tenant_id, x.company_id, x.fiscal_year });
                table.CheckConstraint("number_positive", "last_number>0");
                table.ForeignKey(
                    name: "FK_journal_numbers_accounting_books_tenant_id_company_id",
                    columns: x => new { x.tenant_id, x.company_id },
                    principalSchema: "finance",
                    principalTable: "accounting_books",
                    principalColumns: new[] { "tenant_id", "company_id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "journal_entries",
            schema: "finance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                revision = table.Column<long>(type: "bigint", nullable: false),
                posting_date = table.Column<DateOnly>(type: "date", nullable: true),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                rounding_policy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                fiscal_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                fiscal_year = table.Column<int>(type: "integer", nullable: true),
                number_sequence = table.Column<long>(type: "bigint", nullable: true),
                journal_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                posted_by_issuer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                posted_by_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_journal_entries", x => x.id);
                table.UniqueConstraint("AK_journal_entries_tenant_id_company_id_id", x => new { x.tenant_id, x.company_id, x.id });
                table.CheckConstraint("journal_revision", "revision>0");
                table.CheckConstraint("journal_state", "(status='Draft' AND fiscal_period_id IS NULL AND fiscal_year IS NULL AND number_sequence IS NULL AND journal_number IS NULL AND posted_at IS NULL AND posted_by_subject IS NULL AND posted_by_issuer IS NULL) OR (status='Posted' AND posting_date IS NOT NULL AND fiscal_period_id IS NOT NULL AND fiscal_year IS NOT NULL AND number_sequence>0 AND journal_number IS NOT NULL AND posted_at IS NOT NULL AND posted_by_subject IS NOT NULL AND posted_by_issuer IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_journal_entries_accounting_books_tenant_id_company_id",
                    columns: x => new { x.tenant_id, x.company_id },
                    principalSchema: "finance",
                    principalTable: "accounting_books",
                    principalColumns: new[] { "tenant_id", "company_id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_journal_entries_fiscal_periods_tenant_id_company_id_fiscal_~",
                    columns: x => new { x.tenant_id, x.company_id, x.fiscal_period_id },
                    principalSchema: "finance",
                    principalTable: "fiscal_periods",
                    principalColumns: new[] { "tenant_id", "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "journal_lines",
            schema: "finance",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                journal_id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                company_id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<int>(type: "integer", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                debit = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: false),
                credit = table.Column<decimal>(type: "numeric(24,6)", precision: 24, scale: 6, nullable: false),
                description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_journal_lines", x => x.id);
                table.CheckConstraint("line_amounts", "debit>=0 AND credit>=0 AND position BETWEEN 1 AND 200");
                table.ForeignKey(
                    name: "FK_journal_lines_accounts_tenant_id_company_id_account_id",
                    columns: x => new { x.tenant_id, x.company_id, x.account_id },
                    principalSchema: "finance",
                    principalTable: "accounts",
                    principalColumns: new[] { "tenant_id", "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_journal_lines_journal_entries_tenant_id_company_id_journal_~",
                    columns: x => new { x.tenant_id, x.company_id, x.journal_id },
                    principalSchema: "finance",
                    principalTable: "journal_entries",
                    principalColumns: new[] { "tenant_id", "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_accounts_tenant_id_company_id_code",
            schema: "finance",
            table: "accounts",
            columns: new[] { "tenant_id", "company_id", "code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_fiscal_periods_tenant_id_company_id_starts_on_ends_on",
            schema: "finance",
            table: "fiscal_periods",
            columns: new[] { "tenant_id", "company_id", "starts_on", "ends_on" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_journal_entries_tenant_id_company_id_created_at_id",
            schema: "finance",
            table: "journal_entries",
            columns: new[] { "tenant_id", "company_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "IX_journal_entries_tenant_id_company_id_fiscal_period_id",
            schema: "finance",
            table: "journal_entries",
            columns: new[] { "tenant_id", "company_id", "fiscal_period_id" });

        migrationBuilder.CreateIndex(
            name: "IX_journal_entries_tenant_id_company_id_fiscal_year_number_seq~",
            schema: "finance",
            table: "journal_entries",
            columns: new[] { "tenant_id", "company_id", "fiscal_year", "number_sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_journal_entries_tenant_id_company_id_journal_number",
            schema: "finance",
            table: "journal_entries",
            columns: new[] { "tenant_id", "company_id", "journal_number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_journal_entries_tenant_id_company_id_status_posting_date",
            schema: "finance",
            table: "journal_entries",
            columns: new[] { "tenant_id", "company_id", "status", "posting_date" });

        migrationBuilder.CreateIndex(
            name: "IX_journal_lines_tenant_id_company_id_account_id",
            schema: "finance",
            table: "journal_lines",
            columns: new[] { "tenant_id", "company_id", "account_id" });

        migrationBuilder.CreateIndex(
            name: "IX_journal_lines_tenant_id_company_id_journal_id_position",
            schema: "finance",
            table: "journal_lines",
            columns: new[] { "tenant_id", "company_id", "journal_id", "position" });
        migrationBuilder.Sql(JournalSecuritySql.V1);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new InvalidOperationException("Financial history cannot be dropped by automatic downgrade. Use a reviewed forward recovery migration.");
    }
}
