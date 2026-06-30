using System.Text.Json;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Serialization;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
