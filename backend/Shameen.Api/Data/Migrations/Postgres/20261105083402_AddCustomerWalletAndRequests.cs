using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shameen.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddCustomerWalletAndRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerAccountId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerSessions_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerTransferRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerAccountId = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DestinationCountry = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DestinationCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PayoutCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PayoutMethod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByEmployeeId = table.Column<int>(type: "integer", nullable: true),
                    WalletDebitEntryId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerTransferRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerTransferRequests_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerWalletEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerAccountId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByEmployeeId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerWalletEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerWalletEntries_CustomerAccounts_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_Email",
                table: "CustomerAccounts",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSessions_CustomerAccountId_ExpiresAtUtc",
                table: "CustomerSessions",
                columns: new[] { "CustomerAccountId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSessions_TokenHash",
                table: "CustomerSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTransferRequests_CustomerAccountId_CreatedAtUtc",
                table: "CustomerTransferRequests",
                columns: new[] { "CustomerAccountId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerTransferRequests_Reference",
                table: "CustomerTransferRequests",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWalletEntries_CustomerAccountId_CurrencyCode_Status",
                table: "CustomerWalletEntries",
                columns: new[] { "CustomerAccountId", "CurrencyCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWalletEntries_Reference",
                table: "CustomerWalletEntries",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerSessions");

            migrationBuilder.DropTable(
                name: "CustomerTransferRequests");

            migrationBuilder.DropTable(
                name: "CustomerWalletEntries");

            migrationBuilder.DropTable(
                name: "CustomerAccounts");
        }
    }
}
