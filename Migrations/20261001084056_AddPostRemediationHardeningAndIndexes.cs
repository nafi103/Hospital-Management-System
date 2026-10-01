using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class AddPostRemediationHardeningAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrescriptionItems_Medicines_MedicineId",
                table: "PrescriptionItems");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorId",
                table: "Appointments");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Bills",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_AppointmentDatetime",
                table: "Appointments",
                column: "AppointmentDatetime");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorId_AppointmentDatetime",
                table: "Appointments",
                columns: new[] { "DoctorId", "AppointmentDatetime" });

            migrationBuilder.AddForeignKey(
                name: "FK_PrescriptionItems_Medicines_MedicineId",
                table: "PrescriptionItems",
                column: "MedicineId",
                principalTable: "Medicines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrescriptionItems_Medicines_MedicineId",
                table: "PrescriptionItems");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_AppointmentDatetime",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorId_AppointmentDatetime",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Bills");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorId",
                table: "Appointments",
                column: "DoctorId");

            migrationBuilder.AddForeignKey(
                name: "FK_PrescriptionItems_Medicines_MedicineId",
                table: "PrescriptionItems",
                column: "MedicineId",
                principalTable: "Medicines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
