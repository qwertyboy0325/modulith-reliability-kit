namespace ModulithReliabilityKit.IntegrationTests.Catalog;

/// <summary>
/// Shares a single Catalog PostgreSQL container across the Catalog reliability test classes.
/// Tests reset the tables at the start of each case, so a shared container stays isolated.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CatalogDatabaseCollection : ICollectionFixture<CatalogDatabaseFixture>
{
    public const string Name = "CatalogDatabase";
}
