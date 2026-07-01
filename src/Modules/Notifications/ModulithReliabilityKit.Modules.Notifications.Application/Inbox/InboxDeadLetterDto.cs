namespace ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

/// <summary>
/// Read model for an inbox dead-letter record, surfaced to operators so a poisoned message can be
/// inspected and (once the downstream cause is fixed) reprocessed.
/// </summary>
public sealed record InboxDeadLetterDto(
    Guid Id,
    Guid OriginalMessageId,
    string Type,
    DateTime OccurredOnUtc,
    int RetryCount,
    string LastError,
    DateTime MovedToDeadLetterOnUtc,
    string ResolutionStatus,
    DateTime? ResolvedOnUtc,
    string? ResolvedBy);
