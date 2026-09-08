---
description: ef core — snake_case, no magic strings, string max length, queries, writes, migrations, transactions
globs: ["**/*.cs", "**/Migrations/**/*.cs"]
always: true
---

# EF Core

Этот файл — правила EF Core: queries, writes, transactions, миграции,
snake_case, string length. Entity configuration — в `entity-models.md`.

## 1. No magic strings

Названия таблиц/схем — через константы:

```csharp
// ✅ Correct
[Table(DatabaseInformation.Tables.Balance, Schema = DatabaseInformation.Schemes.Exchange)]

// ❌ Wrong
[Table("balance", Schema = "exchange")]
```

Константы лежат в `<Project>.Entity.Core`:
- Таблицы — `DatabaseInformation.Tables`.
- Схемы — `DatabaseInformation.Schemes`.
- Прочее — `*Constants.cs`.

---

## 2. Queries

**LINQ** для простых случаев. **SqlKata** через `FromSqlKata` — для CTE,
view, cross-schema join'ов.

```csharp
// ✅ Простой запрос — LINQ
return await dbContext.Set<CatalogItem>()
    .Where(instrument => instrument.ExchangeName == exchangeName)
    .AsNoTracking()
    .ToListAsync(cancellationToken);

// ✅ Сложный запрос — SqlKata
var sqlQuery = new Query("catalog.v_items")
    .When(
        gridifyQuery.MinFundingDeltaPercent.HasValue,
        builder => builder.Where("profit_spread", ">=", gridifyQuery.MinFundingDeltaPercent.Value / 100m));

return await dbContext.CarryPairViewsSet
    .FromSqlKata(sqlQuery)
    .AsNoTracking()
    .GridifyAsync(gridifyQuery, cancellationToken);
```

**Никогда не интерполируй параметры в `WhereRaw`** — используй
`Where("col", ">=", value)` или передавай параметры через биндинги SqlKata.

---

## 3. Writes — только EF методы и Batch

```csharp
// ✅ ExecuteUpdate / ExecuteDelete
await dbContext.Set<Instrument>()
    .Where(instrument => instrument.ExchangeName == exchangeName)
    .ExecuteUpdateAsync(
        setters => setters.SetProperty(instrument => instrument.UpdatedAt, DateTime.UtcNow),
        cancellationToken);

// ✅ Bulk
await dbContext.BulkInsertAsync(entities, cancellationToken);
```

**Raw SQL для write — ЗАПРЕЩЁН.**

---

## 4. Transactions

В high-load всегда думай о scope:

```csharp
await using var transaction = await dbContext.Database.BeginTransactionAsync(
    IsolationLevel.Snapshot, cancellationToken);

try
{
    var instruments = await dbContext.Set<CatalogItem>()
        .FromSqlKata(sqlQuery)
        .AsNoTracking()
        .ToListAsync(cancellationToken);

    await transaction.CommitAsync(cancellationToken);
    return instruments;
}
catch
{
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```

---

## 5. DbContext template

```csharp
[ConnectionString($"{SectionConstants.DatabaseSection}:Inventory")]
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> contextOptions)
    : DbContext(contextOptions)
{
    public DbSet<OrderBook> OrderBooksSet { get; init; } = null!;
    public DbSet<CatalogItem> CatalogItemsSet { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        OrderBook.OnModelEntity(modelBuilder);
        CatalogItem.OnModelEntity(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }
}
```

---

## 6. DbContext placement

```
src/database/<Project>/
├── Contexts/
│   ├── InventoryDbContext.cs
│   ├── ManagerDbContext.cs
│   └── UserDbContext.cs
└── Migrations/
    ├── Inventory/
    │   ├── 20260528_Initial.cs
    │   └── InventoryDbContextModelSnapshot.cs
    ├── Manager/
    └── User/
```

Каждый DbContext — в своём файле в `Contexts/`. Миграции каждого контекста —
в собственной подпапке `Migrations/<ContextName>/`.

---

## 7. DbContext — snake_case naming convention — REQUIRED

Все PostgreSQL объекты (таблицы, колонки, индексы, constraints) — **snake_case**.
PascalCase / camelCase DB-идентификаторы **запрещены**.

**Enforcement — двумя слоями, оба обязательны:**

1. `AddModuleDbContext<T>` (`<Shared>.Persistence`) вызывает
   `.UseSnakeCaseNamingConvention()` на `DbContextOptionsBuilder`. Runtime
   safety-net.
2. Explicit `HasColumnName("snake_case")` на каждом property в entity
   configuration. Это то, что **миграции** видят на design-time.

**При добавлении нового property / entity:**

1. Register DbContext через `AddModuleDbContext<TDbContext>(connectionString)` —
   НЕ raw `AddDbContext`. Helper включает naming convention.
2. Добавь `HasColumnName("snake_case")` в каждый `.Property(...)` call.
3. Добавь `HasDatabaseName("ix_table_columns")` в каждый `.HasIndex(...)` call.
4. Используй `DatabaseInformation.Tables.X` / `DatabaseInformation.Schemes.Y`
   для table + schema names.

**При генерации миграций:**

```bash
dotnet ef migrations add <Name>_InitialSchema \
  --context <DbContext> \
  --project src/modules/<Modules>.<M>/<Modules>.<M>.Infrastructure \
  --startup-project src/host/Hybrid.Migrator
```

**Всегда проверяй** что generated миграция snake_case:

```bash
grep "Column<" .../Migrations/*InitialSchema.cs
# Должно показать: id, created_at, user_id — НЕ Id, CreatedAt, UserId
```

**Запрещено:**

- `AddDbContext<T>(options => options.UseNpgsql(...))` без
  `.UseSnakeCaseNamingConvention()` — используй `AddModuleDbContext<T>`.
- PascalCase column / table names в migration DDL.
- `HasColumnName("PascalCase")` — всегда snake_case.

---

## 8. String properties — REQUIRED explicit length

Каждое `string` property на EF-entity, configured via Fluent API, обязано
иметь **явный upper bound**. Bare string → `text` (PostgreSQL) /
`nvarchar(max)` (SQL Server) — unbounded column, принимает мусор любой
длины, нет бизнес-ограничения в схеме.

Каждый `.Property(x => x.SomeString)` обязан удовлетворять **одному** из:

1. **`.HasMaxLength(n)`** — стандартный случай.
2. **`.HasColumnType("jsonb")`** — column type сам ограничивает форму.
3. **`.HasConversion(...)` + `.HasColumnType("char(N)")`** — value-object
   конверсии.

```csharp
// ✅ Standard — varchar(256)
builder.Property(static c => c.Name)
    .HasColumnName("name")
    .HasMaxLength(256)
    .IsRequired();

// ✅ JSON payload — jsonb controls shape
builder.Property(static m => m.Payload)
    .HasColumnName("payload")
    .HasColumnType("jsonb")
    .IsRequired();

// ✅ Value-object via char(24)
builder.Property(static u => u.AgencyId)
    .HasConversion(static id => id.ToString(), static value => ObjectId.Parse(value))
    .HasColumnName("agency_id")
    .HasColumnType("char(24)")
    .IsRequired();

// ❌ Wrong — unbounded text column
builder.Property(static u => u.SecurityStamp)
    .HasColumnName("security_stamp")
    .IsRequired();
//   missing: .HasMaxLength(64) или .HasColumnType(...)
```

**`IsRequired` is orthogonal.** Контролирует NULLability. `HasMaxLength(n)`
контролирует upper bound. Они независимы.

**Size guide:**

| Field kind | Typical MaxLength |
|------------|-------------------|
| Name / Title | 200–256 |
| Description | 1000–2000 |
| Email | 320 |
| Slug / Code | 64–128 |
| URL | 2048 |
| Hash / Stamp | 64–512 |
| JSON payload | — (use `.HasColumnType("jsonb")`) |
| ObjectId ref | — (use `.HasColumnType("char(24)")`) |

**Forbidden:**

- Bare string property в Fluent API без `.HasMaxLength()` или `.HasColumnType(...)`.
- `[MaxLength]` / `[StringLength]` / `[Required]` DataAnnotations на
  EF-entities. Этот проект использует Fluent API исключительно для entity
  configuration. DataAnnotations — для Options классов и DTO.
- `.HasMaxLength(int.MaxValue)` — defeats the purpose.

**Enforcement:** convention + code review. Нет analyzer для bare string.
**Test:** grep `.HasColumnName\(` после `.Property\(static .*string` —
должен иметь `.HasMaxLength` или `.HasColumnType`.

---

## 9. Migrations

**Никогда вручную с нуля.** Только:

```bash
dotnet ef migrations add <Name> \
    -c <DbContextName> \
    -o ./Migrations/<ContextName> \
    -s ../../application/api/MyProject.Api.Public/MyProject.Api.Public.csproj
```

Ручное редактирование сгенерированного `Up()` / `Down()` для сложного SQL
(views, triggers, partitions) — разрешено.

`[**/Migrations/*.cs]` блок в `.editorconfig` отключает IDE0058 / IDE0161 /
MA0197 / CA1861 / IDE1006 — auto-format drift на миграциях принят.

**См. также:** `process/migrations.md` — Hybrid Migrator CLI, environment selection.

---

## Связанные правила

- `entity-models.md` — entity configuration, OnModelEntity, interfaces
- `di-options.md` — connection strings через IOptions
- `process/migrations.md` — migrator CLI
- `analyzers.md` — analyzer packages