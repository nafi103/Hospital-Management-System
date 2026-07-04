using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class SeedInitialData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Beds",
                columns: new[] { "Id", "BedNumber", "DailyRate", "Status", "Type" },
                values: new object[,]
                {
                    { 1, "ICU-01", 5000m, "Available", "ICU" },
                    { 2, "WD-101", 1000m, "Available", "General Ward" },
                    { 3, "CABIN-05", 3500m, "Occupied", "VIP Cabin" }
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Permissions", "RoleName" },
                values: new object[,]
                {
                    { 1, "All", "Admin" },
                    { 2, "Read, Write_Patient", "Doctor" },
                    { 3, "Read, Write_Admission", "Receptionist" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "Department", "FullName", "PasswordHash", "RoleId", "Username" },
                values: new object[] { 1, "Management", "System Admin", "admin123", 1, "admin" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Beds",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Beds",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Beds",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1);
        }
    }
}
