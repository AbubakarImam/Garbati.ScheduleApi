using Garbati.ScheduleApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Garbati.ScheduleApi.Tests.Data;

// Confirms the migrated database schema actually matches Entities/StandingInstruction.cs and
// Entities/StandingInstructionRun.cs - not just that EF's in-memory model does.
[Collection("Postgres")]
public class MigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task AppliedMigrations_IncludeInitialCreate()
    {
        await using var ctx = fixture.CreateContext();

        var applied = await ctx.Database.GetAppliedMigrationsAsync();

        Assert.Contains(applied, m => m.EndsWith("InitialCreate"));
    }

    [Fact]
    public async Task StandingInstructionsTable_ExistsWithItsExpectedColumns()
    {
        await using var ctx = fixture.CreateContext();

        await AssertColumnsAsync(ctx, "standing_instructions",
            "id", "standing_instruction_id", "name", "customer_id",
            "from_account_type", "from_account_id", "to_account_type", "to_account_id",
            "transfer_type", "instruction_type", "priority", "amount", "currency", "narration",
            "valid_from", "valid_till", "recurrence_type", "recurrence_frequency", "recurrence_interval",
            "recurrence_on_day", "allow_partial_transfer", "status", "last_run_date", "created_at", "updated_at");

        await AssertColumnsAsync(ctx, "standing_instruction_runs",
            "id", "standing_instruction_id", "scheduled_date", "status", "amount", "executed_at",
            "error_message", "idempotency_key", "orchestrator_reference");
    }

    private static async Task AssertColumnsAsync(Microsoft.EntityFrameworkCore.DbContext ctx, string table, params string[] expectedColumns)
    {
        var actual = new List<string>();
        var connection = ctx.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_name = @table";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actual.Add(reader.GetString(0));
        }

        foreach (var expected in expectedColumns)
        {
            Assert.Contains(expected, actual);
        }
    }
}
