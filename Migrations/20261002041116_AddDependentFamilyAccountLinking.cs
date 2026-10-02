using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class AddDependentFamilyAccountLinking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GuardianPatientId",
                table: "Patients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuardianRelationship",
                table: "Patients",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_GuardianPatientId",
                table: "Patients",
                column: "GuardianPatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Patients_GuardianPatientId",
                table: "Patients",
                column: "GuardianPatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Patients_GuardianPatientId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_GuardianPatientId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "GuardianPatientId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "GuardianRelationship",
                table: "Patients");
        }
    }
}
