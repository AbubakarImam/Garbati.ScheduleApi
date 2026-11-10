using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Garbati.ScheduleApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "standing_instructions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    standing_instruction_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<string>(type: "text", nullable: false),
                    from_account_type = table.Column<string>(type: "text", nullable: false),
                    from_account_id = table.Column<string>(type: "text", nullable: false),
                    to_account_type = table.Column<string>(type: "text", nullable: false),
                    to_account_id = table.Column<string>(type: "text", nullable: false),
                    transfer_type = table.Column<string>(type: "text", nullable: false),
                    instruction_type = table.Column<string>(type: "text", nullable: false),
                    priority = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    narration = table.Column<string>(type: "text", nullable: true),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_till = table.Column<DateOnly>(type: "date", nullable: true),
                    recurrence_type = table.Column<string>(type: "text", nullable: false),
                    recurrence_frequency = table.Column<string>(type: "text", nullable: false),
                    recurrence_interval = table.Column<int>(type: "integer", nullable: false),
                    recurrence_on_day = table.Column<int>(type: "integer", nullable: true),
                    allow_partial_transfer = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    last_run_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_standing_instructions", x => x.id);
                    table.CheckConstraint("ck_standing_instructions_from_account_type", "\"from_account_type\" IN ('Savings', 'Loan')");
                    table.CheckConstraint("ck_standing_instructions_instruction_type", "\"instruction_type\" IN ('Fixed', 'Dues')");
                    table.CheckConstraint("ck_standing_instructions_priority", "\"priority\" IN ('Urgent', 'High', 'Medium', 'Low')");
                    table.CheckConstraint("ck_standing_instructions_recurrence_frequency", "\"recurrence_frequency\" IN ('Days', 'Weeks', 'Months', 'Years')");
                    table.CheckConstraint("ck_standing_instructions_recurrence_type", "\"recurrence_type\" IN ('Periodic', 'AsPerDues')");
                    table.CheckConstraint("ck_standing_instructions_status", "\"status\" IN ('Active', 'Disabled', 'Deleted')");
                    table.CheckConstraint("ck_standing_instructions_to_account_type", "\"to_account_type\" IN ('Savings', 'Loan')");
                    table.CheckConstraint("ck_standing_instructions_transfer_type", "\"transfer_type\" IN ('AccountTransfer', 'LoanRepayment', 'ChargePayment', 'InterestTransfer', 'LoanDownPayment')");
                });

            migrationBuilder.CreateTable(
                name: "standing_instruction_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    standing_instruction_id = table.Column<long>(type: "bigint", nullable: false),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    orchestrator_reference = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_standing_instruction_runs", x => x.id);
                    table.CheckConstraint("ck_standing_instruction_runs_status", "\"status\" IN ('Success', 'Partial', 'Failed', 'Skipped')");
                    table.ForeignKey(
                        name: "fk_standing_instruction_runs_standing_instructions_standing_in",
                        column: x => x.standing_instruction_id,
                        principalTable: "standing_instructions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_standing_instruction_runs_idempotency_key",
                table: "standing_instruction_runs",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_standing_instruction_runs_standing_instruction_id",
                table: "standing_instruction_runs",
                column: "standing_instruction_id");

            migrationBuilder.CreateIndex(
                name: "ix_standing_instructions_customer_id",
                table: "standing_instructions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_standing_instructions_name",
                table: "standing_instructions",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_standing_instructions_standing_instruction_id",
                table: "standing_instructions",
                column: "standing_instruction_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "standing_instruction_runs");

            migrationBuilder.DropTable(
                name: "standing_instructions");
        }
    }
}
