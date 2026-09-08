---
description: multi-provider authentication — IAuthProvider abstraction, pluggable schemes (Guest/AdminBearer/LDAP/Keycloak), ASP.NET AddScheme composition, no hand-rolled JWT/Cookie
globs: ["**/*.cs"]
priority: high
---

# Multi-provider authentication

Inbound authentication is a first-class concern for any service with
multi-tenant or enterprise deployment; without a rule, agents reach for
ad-hoc `HttpContext.User` checks and per-controller
`if (user.IsInRole(...))` blocks. This rule establishes the abstraction
and the composition pattern. Library choice is pinned in
`technology-stack.md`.

## 1. The `IAuthProvider` abstraction

Authentication providers implement a kernel-level contract so the
rest of the app sees one shape, regardless of the underlying scheme:

```csharp
public interface IAuthProvider
{
    /// <summary>Stable name used in the `auth:providers:<name>:enabled` config key.</summary>
    string Name { get; }

    /// <summary>
    ///     Authenticate an incoming request and return the resulting
    ///     principal. Returns NoResult when the scheme cannot
    ///     authenticate this request (e.g. anonymous read).
    /// </summary>
    Task<AuthenticateResult> AuthenticateAsync(
        HttpContext context, CancellationToken cancellationToken = default);

    /// <summary>Provider-specific configuration validation.</summary>
    void ValidateOptions(IConfigurationSection section);
}
```

Each provider registers itself via `AddScheme<TOptions, THandler>` so
multiple schemes coexist; `IAuthProvider` is the kernel abstraction
the rest of the app sees, not a specific `AuthenticationHandler`.

## 2. Composition — one installer, multiple schemes

```csharp
public static AuthenticationBuilder AddAppAuthentication(
    this IServiceCollection services, IConfiguration configuration)
{
    var auth = services.AddAuthentication("guest")  // default scheme = guest
        .AddScheme<GuestAuthOptions, GuestAuthHandler>(
            GuestAuthProvider.Name,
            _ => { })
        .AddScheme<AdminBearerOptions, AdminBearerHandler>(
            AdminAuthProvider.Name,
            options => { /* bind from configuration */ })
        .AddScheme<LdapAuthOptions, LdapAuthHandler>(
            LdapAuthProvider.Name,
            options => { /* bind from configuration */ });

    if (configuration["auth:providers:keycloak:enabled"] == "true")
    {
        auth.AddJwtBearer(KeycloakAuthProvider.Name, options =>
        {
            options.Authority = configuration["auth:providers:keycloak:authority"];
            options.TokenValidationParameters = new TokenValidationParameters { ... };
        });
    }

    return auth;
}
```

Default scheme is **guest** (anonymous read for all endpoints
without `[Authorize]`). Admin / LDAP / Keycloak schemes are opt-in
via `auth:providers:<name>:enabled = true` in configuration.

## 3. Config schema

Shape below in the project's config format (`appsettings.json` here; the same
keys apply to any file format the project uses — see the project's
configuration rule). Secret values are **references**, never literals: `env:VAR`
or `file:/path`, resolved at startup.

```json
{
  "auth": {
    "defaultScheme": "guest",
    "providers": {
      "guest":        { "enabled": true },
      "admin-bearer": { "enabled": true, "token": "env:APP_ADMIN_TOKEN" },
      "ldap": {
        "enabled": true,
        "server": "ldap://ldap.example.com",
        "baseDn": "dc=example,dc=com",
        "bindDn": "env:LDAP_BIND_DN",
        "bindPassword": "env:LDAP_BIND_PASSWORD",
        "userFilter": "(uid={0})",
        "roleAttribute": "memberOf"
      },
      "keycloak": {
        "enabled": true,
        "authority": "https://kc.example.com/realms/app",
        "clientId": "app-console",
        "clientSecret": "env:KEYCLOAK_CLIENT_SECRET"
      }
    }
  }
}
```

## 4. Endpoint authorization

```csharp
[ApiController]
[Route(ApiRoutes.Items)]
public sealed class ItemsController(...) : ControllerBase
{
    [HttpGet]
    public async Task<...> ListAsync(...)    // guest OK (no [Authorize])

    [HttpPost]
    [Authorize(Policy = "admin")]              // admin scheme required
    public async Task<...> CreateAsync(...)

    [HttpPut("{itemId:guid}")]
    [Authorize(Roles = "admin,editor")]       // role check via LDAP / Keycloak
    public async Task<...> UpdateAsync(...)
}
```

`[AllowAnonymous]` is explicit; absence of `[Authorize]` means guest
access — do not rely on a global filter, name it.

## 5. Per-user identity — `ICurrentUserContext`

Authenticated requests expose the principal through an injected
context (NOT `HttpContext.User` directly scattered through handlers):

```csharp
public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    string? DisplayName { get; }
    IReadOnlyList<string> Roles { get; }
    string? TenantId { get; }
}
```

Scoped lifetime, populated by an `AuthenticationMiddleware` that reads
`HttpContext.User` once at the start of the request.

## 6. Decision table — which provider for which tenant?

| Tenant shape | Recommended scheme |
|--------------|--------------------|
| Single-tenant self-hosted (no users) | Guest (anonymous read) only |
| Single-tenant with admin settings page | Guest + AdminBearer |
| Enterprise with Active Directory / OpenLDAP | Guest + LDAP |
| Enterprise with SSO (Keycloak / Auth0 / Azure AD) | Guest + Keycloak |
| Mixed (LDAP backend via Keycloak federation) | Guest + Keycloak only — Keycloak federates server-side |

A single project can ship multiple schemes simultaneously
(per-`[Authorize]` policy chooses). Don't pre-optimise — pick the
shape at deploy time.

## 7. Library choices (pinned)

| Concern | Library |
|---------|---------|
| Bearer token validation | `Microsoft.AspNetCore.Authentication.JwtBearer` (BCL) |
| OIDC discovery | `Microsoft.AspNetCore.Authentication.JwtBearer` w/ `Authority` |
| LDAP | `System.DirectoryServices.Protocols` (BCL) — `apk add libldap` on Alpine |
| Cookie | `Microsoft.AspNetCore.Authentication.Cookies` (only when needed) |

`Novell.Directory.Ldap.NETStandard` is also acceptable but not
default — docs + AD coverage are weaker.

## 8. Anti-patterns

```csharp
// ❌ Wrong — hand-rolled JWT validation (banned by technology-stack.md)
var parts = token.Split('.');
var payload = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));

// ❌ Wrong — auth check in handler body
if (!User.Identity?.IsAuthenticated ?? true)
    return Unauthorized();
Use [Authorize] attribute instead.

// ❌ Wrong — credential caching in memory
private static readonly ConcurrentDictionary<string, LdapEntry> _cache = new();
// LDAP provider queries upstream per request — no cache, no caching of credentials.

// ❌ Wrong — a secret literal in a committed config file
"admin-bearer": { "token": "abc123-do-not-commit" }
// Always use env:VAR / file:/path; resolved at startup by the secret resolver.

// ❌ Wrong — HttpContext.User scattered through handlers
public async Task<...> DoThing(ClaimsPrincipal user)
=> await repo.GetAsync(user.FindFirst("tenant")!.Value);
// Use ICurrentUserContext — keeps handlers unit-testable without HTTP plumbing.

// ❌ Wrong — own auth scheme per module
namespace <Solution>.Modules.<X>.Auth { ... }
// Cross-cutting concern belongs in <Solution>.Shared.Authentication only.
```

## 9. Self-audit

```bash
# Should be empty — every project that needs auth registers through
# <Solution>.Shared.Authentication (no per-module AuthenticationHandler).
rg -n "AuthenticationHandler<" src/modules/ --type cs

# [Authorize] attributes always on the action, never on the class
# (see api-design.md).
rg -n "^\s*\[Authorize" src/modules/**/*.cs | rg -v "Http(Get|Post|Put|Delete)"

# No secret literals in committed config files (resolve via env: / file: prefix).
rg -in "token|secret|password" **/appsettings*.json **/app.*.local.*
```

## 10. Related rules

- `technology-stack.md` §"Auth provider" menu — library choices pinned.
- the project's configuration rule — config schema + env override + secret prefixes.
- `secrets.md` — runtime secret handling (env vars + file: refs).
- `api-design.md` §"[Authorize]" placement convention.
- `error-mapping.md` §4 — status-code map for 401/403 ProblemDetails.
- `exceptions.md` §4 — no PII / secrets in error messages.

## 11. Multi-tenancy integration (stretch)

When per-tenant routing is added (single-tenant MVP), the tenant
identifier comes from the auth claim, not the URL. `IAuthProvider`
maps the upstream provider's group attribute (`memberOf`,
`groups`, etc.) into the tenant identifier via project-specific
mapping (see project-local rules — a single-tenant MVP typically
pins a constant tenant id and defers multi-tenancy).
