using ModulithReliabilityKit.BuildingBlocks.Application.Queries;

namespace ModulithReliabilityKit.Modules.Notifications.Application.Inbox.GetInboxDeadLetters;

/// <summary>Lists inbox dead-letters. Unresolved only by default; set <paramref name="IncludeResolved"/> to see all.</summary>
public sealed record GetInboxDeadLettersQuery(bool IncludeResolved = false)
    : IQuery<IReadOnlyCollection<InboxDeadLetterDto>>;
