using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shameen.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddBranchCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Branches",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "Branches" AS branch
                SET "Country" = (
                    SELECT MIN(city."Country")
                    FROM "DestinationCities" AS city
                    WHERE LOWER(city."City") = LOWER(branch."City")
                )
                WHERE EXISTS (
                    SELECT 1
                    FROM "DestinationCities" AS city
                    WHERE LOWER(city."City") = LOWER(branch."City")
                    GROUP BY LOWER(city."City")
                    HAVING COUNT(DISTINCT city."Country") = 1
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Country",
                table: "Branches");
        }
    }
}
