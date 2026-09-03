using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeVitalsAndAllergyIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PatientVitals_CreatedAt",
                table: "PatientVitals");

            migrationBuilder.DropIndex(
                name: "IX_PatientVitals_PatientId",
                table: "PatientVitals");

            migrationBuilder.DropIndex(
                name: "IX_PatientAllergies_PatientId",
                table: "PatientAllergies");

            migrationBuilder.AddColumn<string>(
                name: "AllergenGenericName",
                table: "PatientAllergies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubstanceNormalized",
                table: "PatientAllergies",
                type: "text",
                nullable: true,
                computedColumnSql: "lower(btrim(\"Substance\"))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientVitals_PatientId_CreatedAt",
                table: "PatientVitals",
                columns: new[] { "PatientId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PatientAllergies_PatientId_SubstanceNormalized",
                table: "PatientAllergies",
                columns: new[] { "PatientId", "SubstanceNormalized" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PatientVitals_PatientId_CreatedAt",
                table: "PatientVitals");

            migrationBuilder.DropIndex(
                name: "IX_PatientAllergies_PatientId_SubstanceNormalized",
                table: "PatientAllergies");

            migrationBuilder.DropColumn(
                name: "SubstanceNormalized",
                table: "PatientAllergies");

            migrationBuilder.DropColumn(
                name: "AllergenGenericName",
                table: "PatientAllergies");

            migrationBuilder.CreateIndex(
                name: "IX_PatientVitals_CreatedAt",
                table: "PatientVitals",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PatientVitals_PatientId",
                table: "PatientVitals",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientAllergies_PatientId",
                table: "PatientAllergies",
                column: "PatientId");
        }
    }
}
