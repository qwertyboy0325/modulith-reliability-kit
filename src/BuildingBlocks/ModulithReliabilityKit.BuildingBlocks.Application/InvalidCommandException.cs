namespace ModulithReliabilityKit.BuildingBlocks.Application;

public sealed class InvalidCommandException : Exception
{
    public InvalidCommandException(IReadOnlyCollection<string> errors)
        : base("Command validation failed.")
    {
        Errors = errors;
    }

    public IReadOnlyCollection<string> Errors { get; }
}
