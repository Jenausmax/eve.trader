---
description: ef core entity models — multi-interface inheritance, OnModelEntity pattern, view configuration, indexes
globs: ["**/*.cs"]
always: true
---

# Entity models

Этот файл — правила конфигурации EF entities. Queries/writes/migrations —
в `ef-core.md`.

## 1. Multi-interface inheritance — REQUIRED

Свойства сущности **наследуются** через интерфейсы, а не объявляются
ad-hoc в каждой модели.

| Interface | Property |
|-----------|----------|
| `IExchangeObject` | `ExchangeName` |
| `IUserObject` | `UserId` |
| `IUpdatedEntity` | `UpdatedAt` |
| `IEntryComputed` | `Id` (computed key) |
| `ISettlementSymbol` | `AssetName` etc. |
| `IInstrumentSeparation` | `InstrumentType` |

```csharp
[Table(DatabaseInformation.Tables.Balance, Schema = DatabaseInformation.Schemes.Exchange)]
public sealed class BalanceInfo : IEntryComputed, IExchangeObject,
    IUpdatedEntity, IUserObject, ISettlementSymbol, IInstrumentSeparation
{
    [Key]
    public string Id { get; init; } = string.Empty;
    public InstrumentType InstrumentType { get; init; }
    public decimal? Value { get; set; }
    public string ExchangeName { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
```

### Когда выносить поле в новый интерфейс

Два условия — **оба** должны быть истинны:

1. Поле встречается в **3+** моделях с одинаковой семантикой
   (не просто одинаковое имя — одинаковый смысл).
2. Существует или явно планируется потребитель, работающий с этими моделями
   **полиморфно** через интерфейс.

```csharp
// ✅ Правильный кейс — есть generic-метод
public Task<T> UpdateTimestampAsync<T>(T entity, CancellationToken cancellationToken)
    where T : class, IUpdatedEntity
{
    entity.UpdatedAt = DateTimeOffset.UtcNow;
    return SaveAsync(entity, cancellationToken);
}

// ❌ Wrong — интерфейс без потребителя, чисто маркерный
public interface INamed { public string Name { get; init; } }
```

---

## 2. `OnModelEntity` pattern — REQUIRED

```csharp
[Table(DatabaseInformation.Tables.OrderBook, Schema = DatabaseInformation.Schemes.Exchange)]
[Index(nameof(ExchangeName), nameof(Symbol))]
public sealed class OrderBook : IExchangeObject, IUpdatedEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; init; }

    public string ExchangeName { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
    public string Symbol { get; init; } = string.Empty;

    /// <inheritdoc cref="DbContext.OnModelCreating" />
    public static ModelBuilder OnModelEntity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderBook>(entity =>
        {
            entity.Property(orderBook => orderBook.UpdatedAt)
                .HasConversion(
                    updatedAt => updatedAt.ToUniversalTime(),
                    updatedAt => updatedAt);

            entity.HasIndex(orderBook => new { orderBook.ExchangeName, orderBook.Symbol });
        });

        return modelBuilder;
    }
}
```

---

## 3. View configuration

```csharp
modelBuilder.Entity<CarryPairView>(entity =>
{
    entity.ToView("v_items", "catalog");
    entity.HasNoKey();
});
```

---

## 4. Indexes

`[Index]` атрибутом для простых случаев, builder в `OnModelEntity` — для
сложных (filtered, partial, computed).

Каждый `.HasIndex(...)` обязан иметь `HasDatabaseName("snake_case")`
(см. `ef-core.md` §7).

---

## 5. String properties

См. `ef-core.md` §8 — каждое string property в `.Property(...)` обязано
иметь `.HasMaxLength(n)` или `.HasColumnType(...)`.

---

## 6. Tables/schemas — через константы

См. `ef-core.md` §1. Никогда raw string literals:

```csharp
// ❌ Wrong
[Table("balance", Schema = "exchange")]

// ✅ Correct
[Table(DatabaseInformation.Tables.Balance, Schema = DatabaseInformation.Schemes.Exchange)]
```

---

## Связанные правила

- `ef-core.md` — DbContext, queries, writes, snake_case, string length
- `naming-and-types.md` — naming, sealed, record vs class
- `class-layout-and-tooling.md` — где разрешены models