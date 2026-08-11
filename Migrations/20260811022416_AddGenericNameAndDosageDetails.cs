using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class AddGenericNameAndDosageDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Dosage",
                table: "PrescriptionItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Duration",
                table: "PrescriptionItems",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Instructions",
                table: "PrescriptionItems",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Medicines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "GenericName",
                table: "Medicines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "GenericName", "Name" },
                values: new object[] { "Paracetamol", "Napa 500mg" });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "GenericName", "Name", "StockQuantity", "UnitPrice" },
                values: new object[] { "Paracetamol", "Ace 500mg", 1500, 2.00m });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "GenericName", "Name", "StockQuantity", "UnitPrice" },
                values: new object[] { "Azithromycin", "Zithromax 500mg", 50, 35.00m });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "GenericName", "Name", "StockQuantity", "UnitPrice" },
                values: new object[] { "Azithromycin", "Azithral 500mg", 300, 25.00m });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "GenericName", "Name", "StockQuantity", "UnitPrice" },
                values: new object[] { "Azithromycin", "Zmax 500mg", 0, 30.00m });

            migrationBuilder.InsertData(
                table: "Medicines",
                columns: new[] { "Id", "CreatedAt", "GenericName", "Name", "StockQuantity", "UnitPrice", "UpdatedAt" },
                values: new object[,]
                {
                    { 6, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Omeprazole", "Seclo 20mg", 600, 5.00m, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 7, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cetirizine", "Alatrol 10mg", 1200, 2.50m, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DropColumn(
                name: "Dosage",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "Duration",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "Instructions",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "GenericName",
                table: "Medicines");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Medicines",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "Paracetamol 500mg");

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Name", "StockQuantity", "UnitPrice" },
                values: new object[] { "Amoxicillin 250mg", 500, 5.00m });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Name", "StockQuantity", "UnitPrice" },
                values: new object[] { "Ibuprofen 400mg", 800, 3.00m });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Name", "StockQuantity", "UnitPrice" },
                values: new object[] { "Omeprazole 20mg", 600, 4.50m });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Name", "StockQuantity", "UnitPrice" },
                values: new object[] { "Cetirizine 10mg", 1200, 1.50m });
        }
    }
}
