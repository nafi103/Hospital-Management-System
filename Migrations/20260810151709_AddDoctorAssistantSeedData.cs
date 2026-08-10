using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorAssistantSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 3,
                column: "RoleName",
                value: "Assistant");

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "AssignedDoctorId", "Category", "CreatedAt", "FullName", "Password", "RoleId", "UpdatedAt", "Username" },
                values: new object[] { 100, null, "Consultant", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Dr. Mock", "password123", 2, new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "drmock" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 100);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 3,
                column: "RoleName",
                value: "Receptionist");
        }
    }
}
