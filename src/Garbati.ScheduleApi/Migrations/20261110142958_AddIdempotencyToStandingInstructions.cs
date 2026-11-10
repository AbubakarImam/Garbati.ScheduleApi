using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garbati.ScheduleApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyToStandingInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "standing_instructions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "initiated_by",
                table: "standing_instructions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_hash",
                table: "standing_instructions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_standing_instructions_initiated_by_idempotency_key",
                table: "standing_instructions",
                columns: new[] { "initiated_by", "idempotency_key" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_standing_instructions_initiated_by_idempotency_key",
                table: "standing_instructions");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "standing_instructions");

            migrationBuilder.DropColumn(
                name: "initiated_by",
                table: "standing_instructions");

            migrationBuilder.DropColumn(
                name: "request_hash",
                table: "standing_instructions");
        }
    }
}
