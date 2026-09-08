---
description: ef core owned types preferred over manual JSON parse for polymorphic stored values (settings, feature flags, audit payloads); open polymorphic collections use one `json` column, never `jsonb`
globs: ["**/*.cs"]
priority: medium
---

# EF Core owned types — preferred over manual JSON parse

## When to use

Polymorphic stored values (settings, feature flags, audit payloads with a shape
contract) MUST use EF Core `OwnsOne` / `OwnsMany` with `HasDiscriminator`
instead of manually serialising to a JSON column and parsing on the way out.

## Anti-patterns (forbidden)

```csharp
// ❌ Wrong — manual JSON parse in domain code, JSON round-trip on every read
public ISetting AsSetting(SettingKey key)
{
    using var doc = JsonDocument.Parse(ValueJson);
    return Materialise(TypeName, key, doc.RootElement.Clone());
}

// ❌ Wrong — pragma to hide unavoidable sync I/O over a string
#pragma warning disable VSTHRD103
var valueJson = JsonSerializer.Serialize(setting, SerializerOptions);
#pragma warning restore VSTHRD103

// ❌ Wrong — string column "type" + string column "value_json", parsed at every read
public string Type { get; set; }
public string ValueJson { get; set; }
```

`JsonDocument.Parse(string)` and `JsonSerializer.Serialize<T>(T)` are
**unavoidably synchronous** — there is no async variant for parsing from a
string (only for `PipeReader` streams). Hiding the resulting `VSTHRD103` with a
pragma is a smell: the code is doing by hand what EF Core does for free.

## Correct pattern

```csharp
// ✅ Domain entity — typed property, EF hydrates directly
public sealed class FeatureFlagEntity
{
    public string OwnerId { get; set; } = "";
    public string SettingKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Owned — discriminated union via EF HasDiscriminator
    public ISetting Setting { get; set; } = null!;
}
```

EF mapping (one column per concrete setting type, discriminator picks
which one is populated):

```csharp
b.Entity<FeatureFlagEntity>(e =>
{
    e.ToTable("feature_flags", "owners");
    e.HasKey(x => new { x.OwnerId, x.SettingKey });

    e.Property(x => x.OwnerId).HasColumnName("owner_id").HasMaxLength(24);
    e.Property(x => x.SettingKey).HasColumnName("setting_key").HasMaxLength(128);
    e.Property(x => x.CreatedAt).HasColumnName("created_at");
    e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

    e.OwnsOne(x => x.Setting, s =>
    {
        s.Property(p => p.Key).HasColumnName("setting_key").HasMaxLength(128);

        s.HasDiscriminator<string>("type_name")
            .HasValue<BoolSetting>(nameof(BoolSetting))
            .HasValue<StringListSetting>(nameof(StringListSetting))
            .HasValue<NumberSetting>(nameof(NumberSetting))
            .HasValue<ObjectSetting>(nameof(ObjectSetting));

        // per-type column mapping
        s.Property(typeof(bool), "Value")
            .HasColumnName("value_bool")
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        s.Property(typeof(IReadOnlyList<string>), "Values")
            .HasColumnName("value_string_list")
            .HasColumnType("jsonb");

        s.Property(typeof(decimal), "Value")
            .HasColumnName("value_number")
            .HasColumnType("numeric(38,18)");

        s.Property(typeof(JsonElement), "Value")
            .HasColumnName("value_object")
            .HasColumnType("jsonb");
    });
});
```

## Repository side

The repository reads/writes the typed property directly — EF hydrates it and
generates the `INSERT` / `UPDATE` per concrete type; no manual serialize, no
discriminator switch in domain code.

```csharp
// ✅ Repository — single roundtrip, typed dictionary
public async Task<IReadOnlyDictionary<string, ISetting>> GetAllForOwnerAsync(
    OwnerId ownerId, CancellationToken ct = default)
{
    var ownerIdText = ownerId.Value.ToString();
    return await dbContext.Set<FeatureFlagEntity>()
        .Where(e => e.OwnerId == ownerIdText)
        .ToDictionaryAsync(
            keySelector: e => e.SettingKey,
            elementSelector: e => e.Setting,    // typed — EF already hydrated
            StringComparer.Ordinal,
            cancellationToken: ct);
}

// ✅ SetAsync — no manual serialise, EF generates UPDATE/INSERT
public async Task SetAsync(
    OwnerId ownerId, ISetting setting, CancellationToken ct = default)
{
    var existing = await dbContext.Set<FeatureFlagEntity>()
        .FirstOrDefaultAsync(
            e => e.OwnerId == ownerId.Value.ToString()
              && e.SettingKey == setting.Key.Canonical,
            ct);

    if (existing is not null)
    {
        existing.Setting = setting;       // EF tracks Owned property change
        existing.UpdatedAt = DateTimeOffset.UtcNow;
    }
    else
    {
        await dbContext.Set<FeatureFlagEntity>().AddAsync(new FeatureFlagEntity
        {
            OwnerId = ownerId.Value.ToString(),
            SettingKey = setting.Key.Canonical,
            Setting = setting,             // EF INSERTs with discriminator + value column
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);
    }
}
```

## Why

1. **No `JsonDocument.Parse` in hot path** — EF handles hydration to typed records.
2. **No `pragma VSTHRD103`** — no manual sync I/O over a string.
3. **Type safety** — adding a new subtype fails compile if not mapped (a missing
   `HasValue<NewType>` or column binding is a build/model error, not a runtime
   surprise).
4. **Single source of truth** — column metadata (length, type, nullability) lives
   in the EF config, not scattered across entity + repository + materialise switch.
5. **SQL transparent** — `INSERT` / `UPDATE` per type is generated by EF; the
   discriminator is one column update, not a JSON rewrite.
6. **No race conditions in the materialise switch** — the switch is removed
   entirely; EF does the polymorphism.

## Materialise — when still needed

A hand-written materialise helper is still legitimate on the **request boundary**:
a client sends a setting as raw JSON in a POST/PUT body and the controller turns
it into a typed value before handing it to the handler. That path is one-shot,
not the read hot path.

## When NOT owned types

- **Truly free-form JSON** with no shape contract (audit events, user-typed
  content). Use a JSON column + `JsonElement` directly. No domain entity for
  these.
- **Migration cost too high** — see the project's migration rule for hybrid
  strategies. Owned is preferred but not retroactive for legacy tables without
  a strong reason.
- **Cross-aggregate composition** — owned types only model "one owned entity per
  parent". For many-to-one polymorphic collections, use a dedicated aggregate
  (e.g. a `SettingEntry` table) with its own repository.
- **Open, growing polymorphic settings collection** — see the exception below.

## Exception — open polymorphic settings collection (JSON bag)

The discriminator-column pattern above is for a **fixed** set of value shapes
(bool / string-list / number / object). For an **open, growing** collection of
polymorphic settings — a per-entity settings bag where each new dimension is a
plugin — the per-type-column TPH does **not** scale:

- every new dimension adds sparse nullable columns that are NULL for every
  other row (the "130-field god-class" transposed into a table);
- every new dimension needs a schema migration, defeating the "a plugin is a
  record + a contributor, touch nothing else" goal;
- the entity↔setting bridge (`ToSetting()` + a materialise switch per type)
  is hand-maintained duplication.

For this case, store the whole setting in **one `json` column** (see the `jsonb`
caveat below) via an EF `HasConversion`, serialised with a **single project-wide
polymorphic System.Text.Json contract** declared next to the setting interface.
Discipline that makes this safe (all four MUST hold):

1. **One serialization contract, three consumers.** The same type discovery +
   polymorphism resolver drives the EF converter, the HTTP wire
   (`ConfigureHttpJsonOptions`), and the OpenAPI schema transformer. One
   discriminator vocabulary (`$type`), one place drift can occur.
2. **The discriminator is pinned, not conventional.** `$type` derives from the
   type's own stable key (a `static readonly` per type), never from
   `nameof(Type)` — renaming a record must not silently break stored rows or
   generated FE clients.
3. **FE types are guaranteed at build time.** The OpenAPI transformer emits the
   full `oneOf` + discriminator so the generated FE union always knows every
   concrete subtype — the runtime resolver alone is NOT sufficient (build-time
   document generation does not honour resolver-added subtypes).
4. **Column type is `json`, NOT `jsonb`.** System.Text.Json's polymorphic reader
   requires the `$type` discriminator to be the **first** property; `jsonb`
   re-sorts object keys on storage (by key length, then value), which moves
   `$type` off the front and makes reads throw *"must specify a type
   discriminator"*. `json` preserves the exact serialized text. This is only
   safe because such a column is never queried into (no need for jsonb
   operators or indexes).

This is **not** a licence for the forbidden anti-patterns above (a bare
`string type` + `string value_json` split parsed by hand in domain/repository
code). The value stays typed, the conversion lives in one EF converter, and no
`JsonDocument.Parse` appears in domain or repository code.

## Enforcement

- **Build gate**: the solution-level build (analyzers + format).
- **Code review**: any new `JsonDocument.Parse` / `JsonSerializer.Serialize` on
  an entity column, paired with a discriminator string, is a reviewer-flagged
  pattern that should be replaced by an owned type.
- **Architecture test** (recommended): grep for `using var doc = JsonDocument.Parse`
  under `Domain/Entities/` and `Application/Persistence/` — should be empty
  except for the request-boundary materialise layer.

## Related

- `ef-core.md` — entity configuration, string length, JSON columns
- `json-and-ndjson.md` — free-form JSON handling, `JsonSerializerOptions`
