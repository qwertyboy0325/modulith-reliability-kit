using System.Reflection;

namespace ModulithReliabilityKit.Modules.Catalog.Application;

/// <summary>
/// Anchor type so the host can register this assembly with MediatR/FluentValidation
/// without leaking concrete handler types.
/// </summary>
public static class CatalogApplicationAssembly
{
    public static readonly Assembly Assembly = typeof(CatalogApplicationAssembly).Assembly;
}
