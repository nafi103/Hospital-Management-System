using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalDataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF renamed Users.Password -> Users.PasswordHash automatically, which
            // carries the existing plaintext value forward under the new column name.
            // It is still plaintext at this point; the crypt() backfill below turns it
            // into a real bcrypt hash before anything reads it as one.
            migrationBuilder.RenameColumn(
                name: "Password",
                table: "Users",
                newName: "PasswordHash");

            migrationBuilder.AddColumn<decimal>(
                name: "DoseAfternoon",
                table: "PrescriptionItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DoseEvening",
                table: "PrescriptionItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DoseMorning",
                table: "PrescriptionItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DoseUnit",
                table: "PrescriptionItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DurationDays",
                table: "PrescriptionItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Route",
                table: "PrescriptionItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Consciousness",
                table: "PatientVitals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "OnSupplementalOxygen",
                table: "PatientVitals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RespiratoryRate",
                table: "PatientVitals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Strength",
                table: "Medicines",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TherapeuticClass",
                table: "Medicines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "MedicalRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    DoctorId = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<int>(type: "integer", nullable: true),
                    ChiefComplaint = table.Column<string>(type: "text", nullable: true),
                    Diagnosis = table.Column<string>(type: "text", nullable: false),
                    Treatment = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalRecords_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MedicalRecords_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MedicalRecords_Users_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientAllergies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    Substance = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ReactionType = table.Column<string>(type: "text", nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    RecordedById = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientAllergies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientAllergies_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PatientAllergies_Users_RecordedById",
                        column: x => x.RecordedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Strength", "TherapeuticClass" },
                values: new object[] { "500mg", "Analgesic/Antipyretic" });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Strength", "TherapeuticClass" },
                values: new object[] { "500mg", "Analgesic/Antipyretic" });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "Strength", "TherapeuticClass" },
                values: new object[] { "500mg", "Macrolide Antibiotic" });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Strength", "TherapeuticClass" },
                values: new object[] { "500mg", "Macrolide Antibiotic" });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "Strength", "TherapeuticClass" },
                values: new object[] { "500mg", "Macrolide Antibiotic" });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "Strength", "TherapeuticClass" },
                values: new object[] { "20mg", "Proton Pump Inhibitor" });

            migrationBuilder.UpdateData(
                table: "Medicines",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "Strength", "TherapeuticClass" },
                values: new object[] { "10mg", "Antihistamine" });

            // --- Data backfill, run before any source column is dropped ---

            // 1. Hash whatever plaintext currently sits in PasswordHash (post-rename) for
            //    every user, seeded or created later through the app. pgcrypto's bf hashes
            //    are standard bcrypt and verify fine with BCrypt.Net's BCrypt.Verify().
            migrationBuilder.Sql(@"CREATE EXTENSION IF NOT EXISTS pgcrypto;");
            migrationBuilder.Sql(@"UPDATE ""Users"" SET ""PasswordHash"" = crypt(""PasswordHash"", gen_salt('bf'));");

            // 2. Parse the legacy ""M + A + E"" dose string and bare-integer duration into
            //    the new structured columns. Falls back to stashing the raw text in
            //    Instructions when a row doesn't match the expected shape, so nothing is
            //    silently lost.
            migrationBuilder.Sql(@"
                UPDATE ""PrescriptionItems""
                SET
                    ""DoseMorning"" = CASE WHEN ""Dosage"" ~ '^\s*\d+(\.\d+)?\s*\+\s*\d+(\.\d+)?\s*\+\s*\d+(\.\d+)?\s*$'
                        THEN split_part(""Dosage"", '+', 1)::numeric ELSE 0 END,
                    ""DoseAfternoon"" = CASE WHEN ""Dosage"" ~ '^\s*\d+(\.\d+)?\s*\+\s*\d+(\.\d+)?\s*\+\s*\d+(\.\d+)?\s*$'
                        THEN split_part(""Dosage"", '+', 2)::numeric ELSE 0 END,
                    ""DoseEvening"" = CASE WHEN ""Dosage"" ~ '^\s*\d+(\.\d+)?\s*\+\s*\d+(\.\d+)?\s*\+\s*\d+(\.\d+)?\s*$'
                        THEN split_part(""Dosage"", '+', 3)::numeric ELSE 0 END,
                    ""DurationDays"" = CASE WHEN trim(""Duration"") ~ '^\d+$'
                        THEN trim(""Duration"")::integer ELSE NULL END,
                    ""Instructions"" = CASE WHEN ""Dosage"" ~ '^\s*\d+(\.\d+)?\s*\+\s*\d+(\.\d+)?\s*\+\s*\d+(\.\d+)?\s*$'
                             AND trim(""Duration"") ~ '^\d+$'
                        THEN ""Instructions""
                        ELSE trim(both ' ' from coalesce(""Instructions"", '') || ' [Original dose: ' || ""Dosage"" || ', duration: ' || ""Duration"" || ']')
                    END;
            ");

            // 3. Backfill MedicalRecords from the legacy per-patient JSON blob. Guards
            //    against NULL/empty/malformed values, since jsonb casts fail hard on them.
            migrationBuilder.Sql(@"
                INSERT INTO ""MedicalRecords"" (""PatientId"", ""DoctorId"", ""Diagnosis"", ""Treatment"", ""RecordedAt"", ""CreatedAt"", ""UpdatedAt"")
                SELECT
                    p.""Id"",
                    (elem->>'DoctorId')::integer,
                    coalesce(elem->>'Diagnosis', ''),
                    coalesce(elem->>'Treatment', ''),
                    coalesce((elem->>'Date')::timestamptz, now()),
                    now(),
                    now()
                FROM ""Patients"" p,
                     jsonb_array_elements(
                        CASE
                            WHEN p.""MedicalHistoryJson"" IS NOT NULL
                                 AND trim(p.""MedicalHistoryJson"") ~ '^\['
                            THEN p.""MedicalHistoryJson""::jsonb
                            ELSE '[]'::jsonb
                        END
                     ) AS elem
                WHERE (elem->>'DoctorId') ~ '^\d+$'
                  AND EXISTS (SELECT 1 FROM ""Users"" u WHERE u.""Id"" = (elem->>'DoctorId')::integer);
            ");

            // Now safe to drop: their data has already been read into the new columns above.
            migrationBuilder.DropColumn(
                name: "Dosage",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "Duration",
                table: "PrescriptionItems");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecords_AppointmentId",
                table: "MedicalRecords",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecords_DoctorId",
                table: "MedicalRecords",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecords_PatientId",
                table: "MedicalRecords",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalRecords_RecordedAt",
                table: "MedicalRecords",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PatientAllergies_PatientId",
                table: "PatientAllergies",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientAllergies_RecordedById",
                table: "PatientAllergies",
                column: "RecordedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicalRecords");

            migrationBuilder.DropTable(
                name: "PatientAllergies");

            migrationBuilder.DropColumn(
                name: "DoseAfternoon",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "DoseEvening",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "DoseMorning",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "DoseUnit",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "DurationDays",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "Route",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "Consciousness",
                table: "PatientVitals");

            migrationBuilder.DropColumn(
                name: "OnSupplementalOxygen",
                table: "PatientVitals");

            migrationBuilder.DropColumn(
                name: "RespiratoryRate",
                table: "PatientVitals");

            migrationBuilder.DropColumn(
                name: "Strength",
                table: "Medicines");

            migrationBuilder.DropColumn(
                name: "TherapeuticClass",
                table: "Medicines");

            // NOTE: irreversible in substance — PasswordHash holds bcrypt hashes by this
            // point and there is no way back to the original plaintext. This only restores
            // the column name and the three seeded accounts' original demo passwords.
            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "Users",
                newName: "Password");

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

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "Password",
                value: "admin123");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 100,
                column: "Password",
                value: "password123");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1010,
                column: "Password",
                value: "password123");
        }
    }
}
