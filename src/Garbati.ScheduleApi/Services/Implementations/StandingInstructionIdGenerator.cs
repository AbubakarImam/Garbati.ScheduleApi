namespace Garbati.ScheduleApi.Services.Implementations;

// Same approach as Garbati.OrchestratorApi's OperationIdGenerator / LedgerApi's TransactionReference /
// IdentityApi's CustomerIdGenerator: an internal, human-readable, generated public identifier - never a
// database primary key exposed directly (see Entities/StandingInstruction.cs's comment on Id vs
// StandingInstructionId). No existing platform convention names a prefix for this entity, so "SI" is this
// task's own choice (Standing Instruction) - see the PR description.
public static class StandingInstructionIdGenerator
{
    private const string AllowedChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 15;
    private const string Prefix = "SI";

    public static string Generate()
    {
        var id = new char[Length];
        for (var i = 0; i < Length; i++)
        {
            id[i] = AllowedChars[Random.Shared.Next(AllowedChars.Length)];
        }
        return Prefix + "-" + new string(id);
    }
}
