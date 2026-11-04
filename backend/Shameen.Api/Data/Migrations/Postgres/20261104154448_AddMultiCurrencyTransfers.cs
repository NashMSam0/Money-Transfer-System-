using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shameen.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddMultiCurrencyTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Transactions",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "PayoutAmount",
                table: "Transactions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PayoutCurrency",
                table: "Transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<decimal>(
                name: "PayoutUnitsPerUsd",
                table: "Transactions",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "SourceCurrency",
                table: "Transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<decimal>(
                name: "SourceUnitsPerUsd",
                table: "Transactions",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "FloatAdjustments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.CreateTable(
                name: "CurrencyRates",
                columns: table => new
                {
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UnitsPerUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByEmployeeId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyRates", x => x.CurrencyCode);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeCurrencyFloats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperatingDayId = table.Column<long>(type: "bigint", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    FloatAssigned = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FloatBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeCurrencyFloats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeCurrencyFloats_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeCurrencyFloats_OperatingDays_OperatingDayId",
                        column: x => x.OperatingDayId,
                        principalTable: "OperatingDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperatingDayCurrencyFloats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperatingDayId = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BranchBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingFloat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingDayCurrencyFloats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperatingDayCurrencyFloats_OperatingDays_OperatingDayId",
                        column: x => x.OperatingDayId,
                        principalTable: "OperatingDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCurrencyFloats_EmployeeId",
                table: "EmployeeCurrencyFloats",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeCurrencyFloats_OperatingDayId_EmployeeId_CurrencyCo~",
                table: "EmployeeCurrencyFloats",
                columns: new[] { "OperatingDayId", "EmployeeId", "CurrencyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperatingDayCurrencyFloats_OperatingDayId_CurrencyCode",
                table: "OperatingDayCurrencyFloats",
                columns: new[] { "OperatingDayId", "CurrencyCode" },
                unique: true);

            migrationBuilder.Sql("UPDATE \"Transactions\" SET \"PayoutAmount\" = \"Amount\" WHERE \"PayoutAmount\" = 0;");
            migrationBuilder.Sql("INSERT INTO \"CurrencyRates\" (\"CurrencyCode\", \"Name\", \"UnitsPerUsd\", \"UpdatedAtUtc\") VALUES ('USD', 'US Dollar', 1, NOW()) ON CONFLICT (\"CurrencyCode\") DO NOTHING;");
            migrationBuilder.Sql("INSERT INTO \"OperatingDayCurrencyFloats\" (\"OperatingDayId\", \"CurrencyCode\", \"OpeningFloat\", \"BranchBalance\") SELECT day.\"Id\", 'USD', day.\"OpeningFloat\", GREATEST(0, day.\"OpeningFloat\" - COALESCE((SELECT SUM(employee.\"FloatAssigned\") FROM \"Employees\" employee WHERE employee.\"FloatEnteredDayId\" = day.\"Id\"), 0)) FROM \"OperatingDays\" day ON CONFLICT (\"OperatingDayId\", \"CurrencyCode\") DO NOTHING;");
            migrationBuilder.Sql("INSERT INTO \"EmployeeCurrencyFloats\" (\"OperatingDayId\", \"EmployeeId\", \"CurrencyCode\", \"FloatAssigned\", \"FloatBalance\") SELECT employee.\"FloatEnteredDayId\", employee.\"Id\", 'USD', employee.\"FloatAssigned\", employee.\"FloatBalance\" FROM \"Employees\" employee WHERE employee.\"FloatEnteredDayId\" IS NOT NULL ON CONFLICT (\"OperatingDayId\", \"EmployeeId\", \"CurrencyCode\") DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurrencyRates");

            migrationBuilder.DropTable(
                name: "EmployeeCurrencyFloats");

            migrationBuilder.DropTable(
                name: "OperatingDayCurrencyFloats");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PayoutAmount",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PayoutCurrency",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PayoutUnitsPerUsd",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SourceCurrency",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SourceUnitsPerUsd",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "FloatAdjustments");
        }
    }
}
