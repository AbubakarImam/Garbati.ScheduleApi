namespace Garbati.ScheduleApi.Exceptions;

public class StandingInstructionNotFoundException(string standingInstructionId)
    : Exception($"Standing instruction '{standingInstructionId}' was not found.");
