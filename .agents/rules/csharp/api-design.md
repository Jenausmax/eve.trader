---
description: asp.net core mvc controllers — [ApiController] skeleton, openapi attrs, [ProducesResponseType] 2xx per-endpoint + transformer for 4xx/5xx, ActionResult<T> signatures, hybrid resource/lifecycle split, url-prefix versioning
globs: ["**/*Controller.cs"]
---

# API design — controllers (ASP.NET Core MVC)

The controllers counterpart to the minimal-API rules. Routes-as-constants →
`api-route-constants.md`; error-response shape → `problem-details.md`;
exception → status → `error-mapping.md`. This file owns controller skeleton,
OpenAPI attributes, response-type documentation, and the resource/lifecycle
split.

Applies only where the project ships MVC controllers. A minimal-API project
uses the files above and ignores this one.

## 1. Controller skeleton

```csharp
[ApiController]
[Route($"{ApiRoutes.Base}/catalog/items")]
public sealed class CatalogItemController(IItemQueryService itemQueryService)
    : ControllerBase
{
    [HttpGet("page")]
    [EndpointName("catalog-item-page")]
    [EndpointSummary("Returns a paginated page of catalog items")]
    [EndpointDescription("Supports filtering and sorting through the query parameters")]
    [Tags(["catalog", "items"])]
    [ProducesResponseType<PageResult<CatalogItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PageResult<CatalogItem>>> GetItemPageAsync(
        [FromQuery] FilterQuery query,
        CancellationToken cancellationToken = default)
    {
        return Ok(await itemQueryService.QueryAsync(query, cancellationToken));
    }
}
```

`sealed` class, primary-constructor injection, `ControllerBase` (not
`Controller` — no views). Route composed from an `ApiRoutes` constant, never a
literal (`api-route-constants.md`).

## 2. OpenAPI attributes

| Metadata | Attribute | Example |
|----------|-----------|---------|
| operationId | `[EndpointName]` | `[EndpointName("catalog-item-page")]` |
| summary | `[EndpointSummary]` | `[EndpointSummary("Returns a paginated…")]` |
| description | `[EndpointDescription]` | `[EndpointDescription("Supports…")]` |
| tags | `[Tags]` | `[Tags(["catalog"])]` |
| response type | `[ProducesResponseType<T>]` | see §3 |

**Banned:** `[SwaggerOperation]` from `Swashbuckle.AspNetCore.Annotations`.

## 3. `[ProducesResponseType<T>]` — 2xx per-endpoint, 4xx/5xx global

- **2xx success** (200/201/202/204): `[ProducesResponseType<T>]` is **required
  per endpoint**.
- **4xx/5xx errors** (400, 404, 409, 422, 500, 503): wired **globally**, not per
  action. `AddProblemDetails()` writes the body (`problem-details.md`); to also
  *document* them in the OpenAPI spec, register an `IOpenApiOperationTransformer`
  in `AddOpenApi(options => options.AddOperationTransformer<...>())` that fills
  the standard set — 400 (`ValidationProblemDetails`), 404, 409, 500, plus
  401/403 when the endpoint has `[Authorize]`. A per-endpoint
  `[ProducesResponseType]` always wins; the transformer only fills empty slots.
- **Override per endpoint** only when an error carries a specific shape (e.g. a
  409 with a machine-readable `code`).

```csharp
// ✅ 200 documented per-endpoint; 404 flows from AddProblemDetails() + transformer
[HttpGet("{userId:guid}")]
[EndpointName("users-get-by-id")]
[ProducesResponseType<UserSummary>(StatusCodes.Status200OK)]
public async Task<ActionResult<UserSummary>> GetUserAsync(
    Guid userId,
    CancellationToken cancellationToken = default)
    => await userService.GetAsync(userId, cancellationToken) is { } user
        ? Ok(user)
        : NotFound();

// ❌ ProblemDetails repeated on every action — copy-paste noise
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]

// ❌ ProblemDetails as a success type
[ProducesResponseType<ProblemDetails>(StatusCodes.Status200OK)]
```

## 4. Endpoint signatures

- Return `ActionResult<T>` (or `IActionResult` for no-body results like 204).
- `CancellationToken cancellationToken = default` **last**, threaded into the
  call.
- `await` always — `.Result` / `.Wait()` banned.
- A one-line pass-through stays `async` + `Ok(await ...)` — otherwise the
  `Task<ActionResult<T>>` signature doesn't line up.

## 5. Routing — compose from the `ApiRoutes` constant

Full rule in `api-route-constants.md`; the controller specifics:

```csharp
public static class ApiRoutes
{
    public const string ApiVersion = "v1";
    public const string Base = "api/" + ApiVersion;   // const expression → "api/v1"
}

[ApiController]
[Route($"{ApiRoutes.Base}/tasks")]     // → /api/v1/tasks
public sealed class TasksController(ITaskService taskService) : ControllerBase
{
    [HttpGet("{taskId:guid}")]         // GET /api/v1/tasks/{taskId}
    [EndpointName("tasks-get")]
    public Task<ActionResult<TaskResponse>> GetAsync(Guid taskId, ...) { ... }
}
```

### Route constraints — required for typed ids

| Segment | Meaning |
|---------|---------|
| `{taskId:guid}` | Guid only |
| `{slug:length(2,50)}` | 2–50 characters |
| `{slug:regex(^[a-z0-9-]+$)}` | kebab-case |
| `{page:int:min(1)}` | int ≥ 1 |

### Resource naming — plural, kebab-case

| Singular (wrong) | Plural (right) |
|------------------|----------------|
| `/api/v1/task` | `/api/v1/tasks` |
| `/api/v1/executionPlan` | `/api/v1/execution-plans` |

Multi-word resources are **kebab-case** in the URL.

**Exceptions:** action verbs (`/cancel`, `/retry`), health probes (`/health`),
`by-...` lookups (`/by-slug/...`).

Don't hardcode `"api/v1"` in `[Route]` and don't concatenate URLs
(`Created($"/api/v1/tasks/{id}", ...)`) — use `CreatedAtAction` with an
`[HttpGet(Name = ...)]` route name.

## 6. Hybrid — resource controller + lifecycle controller

- **Resource controller** — `{Resource}Controller`, standard CRUD.
- **Lifecycle controller** — `{Domain}{Action}Controller` /
  `{Actor}{Action}Controller`: a state machine (claim → heartbeat → release), a
  different client (worker SDK vs dashboard), cross-resource, or non-RESTful
  semantics.

| Controller | Routes | Kind |
|------------|--------|------|
| `RunsController` | `GET/POST /api/v1/runs`, `GET /api/v1/runs/{id}` | Resource CRUD |
| `WorkersController` | `GET /api/v1/workers`, `GET /api/v1/workers/{id}` | Resource CRUD |
| `WorkerClaimController` | `POST /api/v1/workers/claim`, `POST /api/v1/workers/{id}/heartbeat` | Lifecycle |

**Split signal** (any of): different client, state machine (not CRUD),
cross-resource, non-RESTful. A CRUD controller and its lifecycle controller
coexist.

## 7. Endpoint attributes — what is required where

**Required (per endpoint):**
- `[HttpGet]` / `[HttpPost]` / `[HttpPut]` / `[HttpDelete]` / `[HttpPatch]` with
  an explicit template when the path isn't obvious from convention.
- `[ProducesResponseType<T2xx>]` for the success shape.

**Recommended (per endpoint):** `[EndpointName]`, `[EndpointSummary]`, `[Tags]`.

**Global (composition root):** `builder.Services.AddProblemDetails();` — all
4xx/5xx bodies; plus the operation transformer from §3 for their documentation.

## 8. Endpoint-only dependencies — `[FromServices]`

A service needed by **one** endpoint goes on the action parameter via
`[FromServices]`, not in the constructor (`anti-patterns.md`).

## 9. DTO records — separate file

Request/response records live under `Application/Models/` (or the project's DTO
folder), one type per file, **never** inline in the controller
(`anti-patterns.md`).

## 10. Versioning — URL prefix

Version is a URL segment, single-sourced in `ApiRoutes` (§5). Bump = change one
constant; every controller composing `[Route($"{ApiRoutes.Base}/...")]` gets the
new prefix. Attribute-based sub-versioning (`[ApiVersion("1.1")]`) is a concern
only when two generations must coexist in one deployment — rare; add it then,
not preemptively.

## 11. Anti-patterns

```csharp
// ❌ Magic strings in the URL
[Route("api/v1/tasks")]

// ❌ A non-const helper in a [Route] attribute (attribute args must be const —
//    compose with $"..." over ApiRoutes.Base)

// ❌ String concatenation for a created location (use CreatedAtAction)
return Created($"/api/v1/tasks/{task.Id}", task);

// ❌ [controller] token combined with another prefix
[HttpGet("v1/special")]   // → /api/v1/tasks/v1/special — a bug

// ❌ Two controllers on the same route (ambiguous — a v2 controller goes on a
//    different Base)

// ❌ 4xx/5xx on every action — copy-paste noise
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(404)]

// ❌ ProblemDetails as a success response type
[ProducesResponseType<ProblemDetails>(StatusCodes.Status200OK)]

// ❌ void action — ASP.NET Core still expects an ActionResult
public async Task DeleteAsync(Guid id) { ... }
```

```bash
rg -n '\[Route\("api/v' src/ --type cs   # hardcoded version in [Route] — should be empty
```

## Related

- `api-route-constants.md` — routes as constants (shared with minimal API)
- `problem-details.md` — RFC 9457 error shape; `error-mapping.md` — status mapping
- `class-layout-and-tooling.md` — no private methods; delegate mapping to file-static
- `anti-patterns.md` — DTO records, `[FromServices]`, validation
- `logging.md` — structured logging; `di-installer.md` — DI registration
