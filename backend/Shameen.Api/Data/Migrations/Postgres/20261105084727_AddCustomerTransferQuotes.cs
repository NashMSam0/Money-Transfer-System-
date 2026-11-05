using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shameen.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddCustomerTransferQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "CustomerTransferRequests",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PayoutAmount",
                table: "CustomerTransferRequests",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "CustomerTransferRequests");

            migrationBuilder.DropColumn(
                name: "PayoutAmount",
                table: "CustomerTransferRequests");
        }
    }
}
