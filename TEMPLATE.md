# Template Usage

This file is the handoff checklist for turning this repository into a new backend.

## Default Template Names

| Concept | Template value | Example replacement |
| --- | --- | --- |
| Product / solution name | `ModulithReliabilityKit` | `AcmeCommerce` |
| Product lowercase name | `modulith_reliability_kit` | `acme_commerce` |
| Product database name | `modulith_reliability_kit` | `acme_commerce` |
| Sample module | `Catalog` | `Inventory` |
| Sample module lowercase name | `catalog` | `inventory` |
| Sample module schema | `catalog` | `inventory` |

## Recommended Bootstrap

Run this immediately after copying the template:

```bash
./scripts/new-project.sh AcmeCommerce Inventory
```

The script updates:

- solution and project filenames
- project references
- C# namespaces
- docs references
- Docker container, volume, database, user, and password defaults
- module folder and class names
- lowercase route/schema references

For multi-word names, lowercase replacements use `snake_case` so PostgreSQL database names, users, schemas,
and Docker volumes stay portable. You can manually change public HTTP routes to kebab-case afterward if your API
style requires it.

## After Bootstrap

```bash
dotnet restore src/AcmeCommerce.sln
dotnet build src/AcmeCommerce.sln
dotnet test src/AcmeCommerce.sln
docker compose -f docker-compose.postgres.yml up -d
dotnet run --project src/Api/AcmeCommerce.Api/AcmeCommerce.Api.csproj --urls http://localhost:5099
```

Replace `AcmeCommerce` with your new product name.

## Manual Review Checklist

- Confirm `src/<Product>.sln` opens in your IDE.
- Confirm `src/Api/<Product>.Api/appsettings.json` uses acceptable local connection defaults.
- Decide whether the renamed sample module should stay as a real module or be rewritten as your first domain module.
- Update product-specific text in this root `README.md`.
- Keep `docs/` and `docs-tw/` if you want the architecture notes; remove them only after extracting the parts your team needs.
- Keep `.config/dotnet-tools.json`; it pins `dotnet-ef`.
- Do not commit `ref/`, `bin/`, or `obj/`.

## Reference Material

`ref/` is an optional, local-only folder for source snapshots used while studying architecture. It is
ignored by git on purpose and is **not** required by a generated project.

The architecture notes in `docs/` and `docs-tw/` are self-contained and anchored on this repo's own `src/`
skeleton (the `Catalog` and `Notifications` sample modules). Where they draw on public prior art, it is
attributed inline (for example, Kamil Grzybek's "Modular Monolith with DDD" public reference).
