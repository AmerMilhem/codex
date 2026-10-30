using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Platform.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class IndexOutboxLeaseRecovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_outbox_lease_recovery",
            schema: "integration",
            table: "outbox",
            columns: new[] { "tenant_id", "company_id", "lease_until" },
            filter: "state = 'Processing'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_outbox_lease_recovery",
            schema: "integration",
            table: "outbox");
    }
}
