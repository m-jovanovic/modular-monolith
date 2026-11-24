# Catalog

Catalog describes models of equipment. The item “18 V cordless drill” can describe many barcoded copies. Lending enrolls each physical copy by calling `ICatalogLookup.GetItemAsync` with the catalog item ID. Only a local query crosses that interface. Availability, reservations and borrowers belong to Lending.

## Items and publication

| Use case | HTTP request | Access and result |
| --- | --- | --- |
| Publish a description | `POST /api/catalog/items` | Catalog staff send `title` and `description`. Returns 201 and the new resource location. |
| Read a description | `GET /api/catalog/items/{itemId}` | Public. Returns the stable ID, title and description, or 404. |
| Search published models | `GET /api/catalog/items?q=drill&page=1&pageSize=25` | Public. Matches literal, case-insensitive text in the title or description. Use `categoryId` to narrow results. |
| Correct a description | `PUT /api/catalog/items/{itemId}` | Catalog staff replace both fields. The item retains its identity. |
| Archive a model | `POST /api/catalog/items/{itemId}/archive` | Catalog staff. The item drops out of discovery. Existing catalog references still resolve. |
| Search the archive | `GET /api/catalog/items/archived?q=drill` | Catalog staff. Supports the same text, category and page filters. |
| Restore publication | `POST /api/catalog/items/{itemId}/restore` | Catalog staff. The item reappears in discovery. |

A title accepts 1–160 characters and a description 1–2000 after trimming. Duplicate titles are allowed because different models can share a name. Results sort by case-insensitive title followed by the item ID. Search uses a page size from 1 to 100. Repeating archive or restore returns 204 while the item exists. Archiving affects catalog visibility only. Existing copies and holds continue to follow Lending's rules.

## Categories and classifications

| Use case | HTTP request | Access and result |
| --- | --- | --- |
| Create a category | `POST /api/catalog/categories` | Catalog staff send `name`. Returns 201 and a location. Duplicate names return 409. |
| Browse categories | `GET /api/catalog/categories` | Public. Returns the categories in alphabetical order. |
| Read a category | `GET /api/catalog/categories/{categoryId}` | Public. Returns the category ID and name, or 404. |
| Classify a model | `PUT /api/catalog/items/{itemId}/categories/{categoryId}` | Catalog staff. Returns 204, including on repeated assignment. |
| Remove a classification | `DELETE /api/catalog/items/{itemId}/categories/{categoryId}` | Catalog staff. Repeating removal returns 204 while the item and category exist. |
| Read a model’s classifications | `GET /api/catalog/items/{itemId}/categories` | Public. A known item with no categories returns an empty array. An unknown item returns 404. |

Category names are trimmed and limited to 80 characters. A unique PostgreSQL index on the lowercase name prevents concurrent requests from creating conflicting entries. The combination of item ID and category ID is a primary key. Both foreign keys stay inside the Catalog schema.

## Contributor entry points

- Start with [CreateItem](../../src/Modules/Catalog/Implementation/Items/CreateItem.cs) for a vertical slice with a thin HTTP adapter, validation and a typed handler. [CatalogLookup](../../src/Modules/Catalog/Implementation/Items/CatalogLookup.cs) implements the cross-module query.
- The module owns `catalog.items`, `catalog.categories` and `catalog.item_categories`, together with their [migrations](../../src/Modules/Catalog/Migrations/). The host supplies a keyed Npgsql data source named `Catalog`. A handler runs using module-scoped credentials.
- Test a change with `dotnet test tests/Modules/Catalog/EquipmentLibrary.Catalog.Tests.csproj -c Release`. The [HTTP suite](../../tests/Modules/Catalog/) uses a real PostgreSQL database and the module routes. It checks anonymous and staff requests, empty and missing resources, archive visibility, literal search, classification retries and a race between equivalent category names.
- Tests start a disposable PostgreSQL container by default. To use an existing test server, set `EQLIB_TEST_PG_ADMIN` to its administrator connection string. The suite creates a randomly named database and runtime role, then removes both after the run. Every HTTP query uses the runtime account.
