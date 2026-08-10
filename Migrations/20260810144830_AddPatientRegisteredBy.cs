using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientRegisteredBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RegisteredById",
                table: "Patients",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_RegisteredById",
                table: "Patients",
                column: "RegisteredById");

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Users_RegisteredById",
                table: "Patients",
                column: "RegisteredById",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Users_RegisteredById",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_RegisteredById",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "RegisteredById",
                table: "Patients");
        }
    }
}
