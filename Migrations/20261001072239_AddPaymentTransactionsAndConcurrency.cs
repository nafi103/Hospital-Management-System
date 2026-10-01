using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentTransactionsAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BedTransfers_Beds_BedId",
                table: "BedTransfers");

            migrationBuilder.AddColumn<DateTime>(
                name: "DispensedAt",
                table: "Prescriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DispensedById",
                table: "Prescriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Appointments",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProcessedById = table.Column<int>(type: "integer", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_Bills_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_Users_ProcessedById",
                        column: x => x.ProcessedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_DispensedById",
                table: "Prescriptions",
                column: "DispensedById");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_BillId",
                table: "PaymentTransactions",
                column: "BillId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_ProcessedById",
                table: "PaymentTransactions",
                column: "ProcessedById");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_TransactionDate",
                table: "PaymentTransactions",
                column: "TransactionDate");

            migrationBuilder.AddForeignKey(
                name: "FK_BedTransfers_Beds_BedId",
                table: "BedTransfers",
                column: "BedId",
                principalTable: "Beds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Prescriptions_Users_DispensedById",
                table: "Prescriptions",
                column: "DispensedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BedTransfers_Beds_BedId",
                table: "BedTransfers");

            migrationBuilder.DropForeignKey(
                name: "FK_Prescriptions_Users_DispensedById",
                table: "Prescriptions");

            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_DispensedById",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "DispensedAt",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "DispensedById",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Appointments");

            migrationBuilder.AddForeignKey(
                name: "FK_BedTransfers_Beds_BedId",
                table: "BedTransfers",
                column: "BedId",
                principalTable: "Beds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
