namespace Garbati.ScheduleApi.Exceptions;

// DESIGN.md Section 2.1: Name is required and unique (Fineract's own real, confirmed `name` field, T-069).
public class DuplicateStandingInstructionNameException(string name)
    : Exception($"A standing instruction named '{name}' already exists.");
