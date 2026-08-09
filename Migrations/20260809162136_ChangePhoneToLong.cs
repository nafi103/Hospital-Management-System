using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class ChangePhoneToLong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Patients\" ALTER COLUMN \"EmergencyContactPhone\" TYPE bigint USING \"EmergencyContactPhone\"::bigint;");
            migrationBuilder.Sql("ALTER TABLE \"Patients\" ALTER COLUMN \"ContactInfo\" TYPE bigint USING \"ContactInfo\"::bigint;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "EmergencyContactPhone",
                table: "Patients",
                type: "text",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "ContactInfo",
                table: "Patients",
                type: "text",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");
        }
    }
}
