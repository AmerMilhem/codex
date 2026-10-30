using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Finance.Infrastructure.Migrations;

public partial class FiscalPeriodCloseV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "close_comment", schema: "finance", table: "fiscal_periods",
            type: "character varying(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>(name: "close_reconciliation", schema: "finance", table: "fiscal_periods",
            type: "text", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "closed_at", schema: "finance", table: "fiscal_periods",
            type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "closed_by_issuer", schema: "finance", table: "fiscal_periods",
            type: "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<string>(name: "closed_by_subject", schema: "finance", table: "fiscal_periods",
            type: "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<long>(name: "revision", schema: "finance", table: "fiscal_periods",
            type: "bigint", nullable: false, defaultValue: 1L);

        // The migration owner sees all tenants during this transaction; FORCE is restored before commit.
        migrationBuilder.Sql("ALTER TABLE finance.fiscal_periods NO FORCE ROW LEVEL SECURITY");
        migrationBuilder.Sql("UPDATE finance.fiscal_periods SET revision=2,closed_at=transaction_timestamp(),closed_by_issuer='migration',closed_by_subject='legacy-closed-period',close_reconciliation='{\"passed\":false,\"legacy\":true}' WHERE status='Closed'");
        migrationBuilder.AddCheckConstraint(name: "period_close_metadata", schema: "finance", table: "fiscal_periods",
            sql: "revision>0 AND ((status='Open' AND revision=1 AND closed_at IS NULL AND closed_by_issuer IS NULL AND closed_by_subject IS NULL AND close_comment IS NULL AND close_reconciliation IS NULL) OR (status='Closed' AND revision=2 AND closed_at IS NOT NULL AND length(closed_by_issuer)>0 AND length(closed_by_subject)>0 AND length(close_reconciliation)>0))");
        migrationBuilder.Sql(PeriodCloseSecuritySql.V1);
        migrationBuilder.Sql("ALTER TABLE finance.fiscal_periods FORCE ROW LEVEL SECURITY");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new InvalidOperationException("Fiscal close history cannot be dropped by automatic downgrade. Use a reviewed forward recovery migration.");
}
