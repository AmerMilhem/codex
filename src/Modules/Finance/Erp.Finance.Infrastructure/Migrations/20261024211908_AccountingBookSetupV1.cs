using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Finance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AccountingBookSetupV1 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "code",
            schema: "finance",
            table: "accounting_books",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "PRIMARY");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "created_at",
            schema: "finance",
            table: "accounting_books",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "created_by_issuer",
            schema: "finance",
            table: "accounting_books",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "created_by_subject",
            schema: "finance",
            table: "accounting_books",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "id",
            schema: "finance",
            table: "accounting_books",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "name",
            schema: "finance",
            table: "accounting_books",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "Legacy Accounting Book");

        migrationBuilder.AddColumn<string>(
            name: "status",
            schema: "finance",
            table: "accounting_books",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Active");

        // Existing rows already define one book per company. The scoped hash is stable
        // across clean/upgrade runs; currency and minor_units are never inferred.
        // FORCE RLS is removed only inside this migration transaction and restored
        // before commit so the non-superuser owner can backfill every tenant.
        migrationBuilder.Sql("ALTER TABLE finance.accounting_books NO FORCE ROW LEVEL SECURITY");
        migrationBuilder.Sql("UPDATE finance.accounting_books SET id=md5('erp.finance.accounting-book.v1:'||tenant_id::text||':'||company_id::text)::uuid WHERE id IS NULL");
        migrationBuilder.AlterColumn<Guid>(
            name: "id", schema: "finance", table: "accounting_books", type: "uuid",
            nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
        // Admin-owned historical fixtures can omit the new ID. Runtime inserts must
        // still carry a verified creator and matching audit/outbox at commit.
        migrationBuilder.Sql("ALTER TABLE finance.accounting_books ALTER COLUMN id SET DEFAULT gen_random_uuid()");

        migrationBuilder.CreateIndex(
            name: "IX_accounting_books_id",
            schema: "finance",
            table: "accounting_books",
            column: "id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_accounting_books_tenant_id_company_id_code",
            schema: "finance",
            table: "accounting_books",
            columns: new[] { "tenant_id", "company_id", "code" },
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "book_setup_valid",
            schema: "finance",
            table: "accounting_books",
            sql: "length(code)>0 AND length(name)>0 AND status='Active' AND ((created_at IS NULL AND created_by_issuer IS NULL AND created_by_subject IS NULL) OR (created_at IS NOT NULL AND length(created_by_issuer)>0 AND length(created_by_subject)>0))");
        migrationBuilder.Sql(AccountingBookSetupSecuritySql.V1);
        migrationBuilder.Sql("ALTER TABLE finance.accounting_books FORCE ROW LEVEL SECURITY");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new InvalidOperationException("Accounting book identity and history cannot be dropped by automatic downgrade. Use a reviewed forward recovery migration.");
    }
}
