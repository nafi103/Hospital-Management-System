using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Hospital_Management_System.Migrations
{
    /// <inheritdoc />
    public partial class AddFefoBatchesAndHashChainedLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_TransactionId",
                table: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_DoctorId_AppointmentDatetime",
                table: "Appointments");

            migrationBuilder.AddColumn<string>(
                name: "PreviousHash",
                table: "PaymentTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RowHash",
                table: "PaymentTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MedicineBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MedicineId = table.Column<int>(type: "integer", nullable: false),
                    BatchNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    QuantityRemaining = table.Column<int>(type: "integer", nullable: false),
                    InitialQuantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicineBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicineBatches_Medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "Medicines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_transaction_id",
                table: "PaymentTransactions",
                column: "TransactionId",
                unique: true,
                filter: "\"TransactionId\" IS NOT NULL");

            // Disambiguate any existing legacy duplicate appointment slots prior to index creation
            migrationBuilder.Sql(@"
                WITH duplicates AS (
                    SELECT ""Id"",
                           ROW_NUMBER() OVER (PARTITION BY ""DoctorId"", ""AppointmentDatetime"" ORDER BY ""Id"" ASC) - 1 as offset_slots
                    FROM ""Appointments""
                    WHERE ""Status"" <> 2
                )
                UPDATE ""Appointments"" a
                SET ""AppointmentDatetime"" = a.""AppointmentDatetime"" + (d.offset_slots * INTERVAL '15 minutes'),
                    ""EndTime"" = a.""EndTime"" + (d.offset_slots * INTERVAL '15 minutes')
                FROM duplicates d
                WHERE a.""Id"" = d.""Id"" AND d.offset_slots > 0;
            ");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_doctor_slot_active",
                table: "Appointments",
                columns: new[] { "DoctorId", "AppointmentDatetime" },
                unique: true,
                filter: "\"Status\" <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_MedicineBatches_ExpiryDate",
                table: "MedicineBatches",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_MedicineBatches_MedicineId_ExpiryDate",
                table: "MedicineBatches",
                columns: new[] { "MedicineId", "ExpiryDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicineBatches");

            migrationBuilder.DropIndex(
                name: "ix_payment_transactions_transaction_id",
                table: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "ix_appointments_doctor_slot_active",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "PreviousHash",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RowHash",
                table: "PaymentTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_TransactionId",
                table: "PaymentTransactions",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorId_AppointmentDatetime",
                table: "Appointments",
                columns: new[] { "DoctorId", "AppointmentDatetime" });
        }
    }
}
