using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignedDoctorIdToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedDoctorId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "AssignedDoctorId",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Users_AssignedDoctorId",
                table: "Users",
                column: "AssignedDoctorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_AssignedDoctorId",
                table: "Users",
                column: "AssignedDoctorId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_AssignedDoctorId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_AssignedDoctorId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AssignedDoctorId",
                table: "Users");
        }
    }
}
