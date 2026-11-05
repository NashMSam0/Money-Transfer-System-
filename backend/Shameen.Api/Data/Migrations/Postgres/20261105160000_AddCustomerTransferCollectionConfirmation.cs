using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shameen.Api.Data;

#nullable disable

namespace Shameen.Api.Data.Migrations.Postgres;

[DbContext(typeof(TransferDbContext))]
[Migration("20261105160000_AddCustomerTransferCollectionConfirmation")]
public sealed class AddCustomerTransferCollectionConfirmation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "CollectedAtUtc", table: "CustomerTransferRequests", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<int>(name: "CollectionConfirmedByEmployeeId", table: "CustomerTransferRequests", type: "integer", nullable: true);
        migrationBuilder.AddColumn<string>(name: "CollectionReference", table: "CustomerTransferRequests", type: "character varying(120)", maxLength: 120, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CollectedAtUtc", table: "CustomerTransferRequests");
        migrationBuilder.DropColumn(name: "CollectionConfirmedByEmployeeId", table: "CustomerTransferRequests");
        migrationBuilder.DropColumn(name: "CollectionReference", table: "CustomerTransferRequests");
    }
}
