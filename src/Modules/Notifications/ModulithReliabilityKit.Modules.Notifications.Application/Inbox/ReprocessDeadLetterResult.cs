namespace ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

/// <summary>Outcome of a dead-letter reprocess request.</summary>
public enum ReprocessDeadLetterOutcome
{
    /// <summary>The original message was requeued (or recreated) as pending for another drain.</summary>
    Requeued,

    /// <summary>The dead-letter was already resolved, or its effect had already landed; nothing to do.</summary>
    AlreadyResolved,

    /// <summary>No dead-letter with the given id exists.</summary>
    NotFound
}

/// <summary>Result of a dead-letter reprocess request.</summary>
public sealed record ReprocessDeadLetterResult(
    ReprocessDeadLetterOutcome Outcome,
    Guid DeadLetterId,
    Guid? OriginalMessageId);
