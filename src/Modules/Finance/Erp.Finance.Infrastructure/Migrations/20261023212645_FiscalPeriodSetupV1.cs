using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Finance.Infrastructure.Migrations;

/// <inheritdoc />
public partial class FiscalPeriodSetupV1 : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "created_at",
            schema: "finance",
            table: "fiscal_periods",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "created_by_issuer",
            schema: "finance",
            table: "fiscal_periods",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "created_by_subject",
            schema: "finance",
            table: "fiscal_periods",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "period_create_metadata",
            schema: "finance",
            table: "fiscal_periods",
            sql: "(created_at IS NULL AND created_by_issuer IS NULL AND created_by_subject IS NULL) OR (created_at IS NOT NULL AND length(created_by_issuer)>0 AND length(created_by_subject)>0)");
        migrationBuilder.Sql(PeriodSetupSecuritySql.V1);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new InvalidOperationException("Fiscal period creation history cannot be dropped by automatic downgrade. Use a reviewed forward recovery migration.");
    }
}
