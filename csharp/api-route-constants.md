---
description: routes as constants (one ApiRoutes source), never magic strings in MapGet/MapPost or [Route]; shared by endpoints, tests, and FE codegen
globs: ["**/*.cs"]
always: true
---

# API route constants

Route templates are constants in one place, not string literals scattered across
endpoints — so endpoints, integration tests, and generated FE clients all
reference a single source.

## 1. One `ApiRoutes` source

```csharp
public static class ApiRoutes
{
    public const string Orders     = "/api/orders";
    public const string Order      = "/api/orders/{orderId}";
    public const string OrderLines = "/api/orders/{orderId}/lines";
    public const string Products   = "/api/products";
    public const string Health     = "/api/health";
}
```

Group per feature. If used in a single file, a `file static class` is fine
(`constructors-and-fields.md`); when tests reference it, a shared `ApiRoutes` class.

## 2. Use it — group base + relative segments

```csharp
var group = app.MapGroup(ApiRoutes.Orders).WithTags("Orders");
group.MapGet("/", ListOrdersAsync);
group.MapGet("/{orderId}", GetOrderAsync);
group.MapGet("/{orderId}/lines", ListOrderLinesAsync);
```

Don't repeat the full literal in each `MapGet`; compose from the group base.
(Controllers: the same principle — route-name/template constants, not literals.)

## 3. Conventions

Plural nouns (`/orders`, `/products`), kebab-case for multi-word
(`/product-categories`), sub-resource via nested path (`/api/orders/{id}/lines`),
auth via attributes not path (no `/api/admin/...`).

## Anti-patterns / self-audit

```bash
rg -n 'Map(Get|Post|Put|Delete)\("/api' src/ --type cs   # literal api route in a Map call
rg -n '\[Route\("/?api' src/ --type cs                    # literal route on a controller
```

## Related

- `error-mapping.md`, `problem-details.md`
