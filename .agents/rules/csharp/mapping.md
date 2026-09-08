---
description: mapping — when to use Mapperly (static 1-to-1) vs hand-written (polymorphic dispatch, cross-aggregate assembly)
globs: ["**/*.cs"]
priority: medium
---

# Mapping — Mapperly, manual projection, or hand-written

## Decision tree

```
1. Static 1-to-1 mapping between two records/classes?
   └── Mapperly ([Mapper] partial class, generated at build-time)

2. EF Entity → API DTO with same shape (1:1 fields, no derivation)?
   └── Mapperly

3. API Request → Command with custom construction?
   └── Mapperly + [MapProperty] for non-default cases

4. Value-object unwrap (e.g. ObjectId → string, Money → decimal)?
   └── Mapperly + [MapProperty] / [UserMapping]

5. Collection-shape change (Set<T> → List<T>, projection)?
   └── Mapperly (in newer versions) or hand-written `.Select(...)` 

6. Cross-aggregate assembly (multiple entities → one DTO)?
   └── Hand-written — Mapperly can't compose

7. Polymorphic dispatch (Setting → one of N DTOs, sealed hierarchy)?
   └── Hand-written switch (sealed hierarchy exhaustiveness required)

8. Dynamic / runtime decision (different mapper per discriminator)?
   └── Hand-written
```

## Anti-patterns

```csharp
// ❌ Wrong — manual 1-to-1 mapping that could be Mapperly
public static SettingDefinitionResponse From(SettingDefinition definition)
{
    return new SettingDefinitionResponse(
        definition.Id.Value.ToString(),       // .Value.ToString() — manual unwrap
        definition.Key,
        definition.Type,
        definition.Description,
        [.. definition.AppliesToEntityTypes], // manual projection
        definition.IsPlatformLevel,
        definition.CreatedAt,
        definition.UpdatedAt);
}

// ❌ Wrong — hand-rolled mapping in controller (besides wiring)
return CreatedAtRoute("settings-get-by-key", new { key = request.Key },
    new SettingDefinitionResponse(
        id.Value.ToString(),
        new SettingKey(request.Key),
        request.Type,
        // ... 8 lines of trivial copy
        ));

// ✅ Correct — Mapperly, generated at build-time, compile-time check
[Mapper]
public partial class SettingDefinitionMapper
{
    public partial SettingDefinitionResponse Map(SettingDefinition source);
    
    public partial IReadOnlyList<SettingDefinitionResponse> Map(
        IReadOnlyList<SettingDefinition> source);
}
```

## Mapperly mechanics

### Identity unwrap with `[MapProperty]`

```csharp
[Mapper]
public partial class SettingDefinitionMapper
{
    [MapProperty(nameof(SettingDefinition.Id) + ".Value", 
                  nameof(SettingDefinitionResponse.Id))]
    public partial SettingDefinitionResponse Map(SettingDefinition source);
}
```

`Id.Value.ToString()` lives in `[UserMapping]` (extension on `SettingDefinitionId`):

```csharp
public static class SettingDefinitionIdMappingExtensions
{
    public static string ToWire(this SettingDefinitionId id) => id.Value.ToString();
}
```

### Collection-shape change

`IReadOnlySet<string>` → `IReadOnlyList<string>` needs a `[UserMapping]` (spread):

```csharp
[UserMapping]
private static IReadOnlyList<string> MapAppliesTo(IReadOnlySet<string> set) 
    => [.. set];
```

Or use `Select` in the call-site — fine for one-off places:

```csharp
return definitions.Select(d => _mapper.Map(d)).ToList();
```

### When to skip the mapper

```csharp
// ❌ Wrong — Mapperly can't dispatch polymorphically on ISetting
public partial SettingResponse Map(ISetting setting);   // won't compile

// ✅ Correct — hand-written dispatcher (sealed hierarchy is exhaustive)
public sealed class SettingResponseMapper
{
    public SettingResponse Map(ISetting setting) => setting switch
    {
        BoolSetting b => new(b.Key.Canonical, nameof(BoolSetting), b.Value),
        StringListSetting s => new(s.Key.Canonical, nameof(StringListSetting), s.Values),
        NumberSetting n => new(n.Key.Canonical, nameof(NumberSetting), n.Value),
        ObjectSetting o => new(o.Key.Canonical, nameof(ObjectSetting), o.Value),
        _ => throw new InvalidOperationException(
            $"Unknown setting type '{setting.GetType().Name}'.")
    };
}
```

## Mapper placement

```
src/modules/<M>/<M>.Application/Mappers/
└── <Aggregate>Mapper.cs    // one per aggregate that needs DTO mapping
```

**One mapper per aggregate**, not per controller. Multiple endpoints that need
the same entity→DTO mapping share one mapper instance via DI registration:

```csharp
// ApplicationInstaller
services.AddSingleton<SettingDefinitionMapper>();
```

Controllers + handlers inject the mapper:

```csharp
public sealed class GetSettingDefinitionByKeyHandler(
    ISettingDefinitionRepository repository,
    SettingDefinitionMapper mapper) : IQueryHandler<...>
{
    public async ValueTask<...> HandleAsync(...)
    {
        return await repository.GetByKeyAsync(...) is { } d
            ? mapper.Map(d)
            : null;
    }
}
```

## When NOT to introduce a mapper

- **One-off mapping** with 1-2 fields and only one call site — manual is
  cheaper than creating a mapper class.
- **Tests** — use Builders (see testing-unit.md), not mappers. Mappers are
  for production code shape transforms, not test data construction.
- **Cross-module DTOs** — mappers should live in the **owning** module
  (per `project-deps-and-tests.md` layer rules), not shared in `Shared/`.

## Enforcement

- **Build gate**: `dotnet build <solution>.slnx -c Debug` — Mapperly is
  source-generator, errors fail the build.
- **Code review**: any `public static From(...)` factory that copies 3+ fields
  is a reviewer-flagged pattern that should become a `[Mapper]` partial.
- **Architecture test** (planned): grep for
  `public static .*Response From(` in `Application/Models/` and
  `Application/Endpoints/` — should be empty except where polymorphism forces
  it.
