using System.Diagnostics;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Diagnostics;

/// <summary>
/// Well-known instrumentation names for the reliability pipeline. The host registers these with
/// OpenTelemetry so the custom metrics and spans are exported; tests reference the meter name to attach
/// a <see cref="System.Diagnostics.Metrics.MeterListener"/>.
/// </summary>
public static class ReliabilityInstrumentation
{
    public const string MeterName = "ModulithReliabilityKit.Reliability";

    public const string ActivitySourceName = "ModulithReliabilityKit.Reliability";

    /// <summary>Shared source for spans around the outbox/inbox drains and the NATS transport.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
