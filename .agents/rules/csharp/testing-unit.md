---
description: unit tests — aaa, naming, theory/inlinedata, shouldly, nsubstitute, test data, anti-patterns
globs: ["tests/**/*.cs", "tests/**/*.csproj"]
always: true
---

# Unit tests

Этот файл — содержание unit-тестов. Stack/pyramid — в `testing-stack-and-pyramid.md`.
Integration-тесты — в `testing-integration.md`.

## 1. Структура: AAA (Arrange / Act / Assert)

Каждый тест — три явно разделённых блока. Разделять пустыми строками
**обязательно** — форма документации.

```csharp
[Fact]
public void ReturnZeroForEmptyOrders()
{
    var calculator = new TotalCalculator();
    var orders = Array.Empty<Order>();

    var total = calculator.Calculate(orders);

    total.ShouldBe(0m);
}
```

Комментарии `// Arrange`, `// Act`, `// Assert` **не пишем** — пустые строки
уже делают структуру очевидной. Комментарии — только если в секции есть
нетривиальные действия, требующие пояснения.

---

## 2. Нейминг тестов

**BDD-стиль:** классы — `<ClassUnderTest>Should`, методы — PascalCase,
человекочитаемое описание — в `[Fact(DisplayName = "...")]`.

```csharp
public sealed class TotalCalculatorShould
{
    [Fact(DisplayName = "Given empty orders, when Calculate is called, then returns zero")]
    public void ReturnZeroForEmptyOrders()
    {
        var calculator = new TotalCalculator();

        var total = calculator.Calculate(Array.Empty<Order>());

        total.ShouldBe(0m);
    }

    [Fact(DisplayName = "Given orders with negative price, when Calculate is called, then throws")]
    public void ThrowWhenOrdersContainNegativePrice()
    {
        var calculator = new TotalCalculator();

        Should.Throw<ArgumentException>(() => calculator.Calculate([new Order(-1m, 1)]));
    }
}
```

**Почему:**
- Метод PascalCase — консистентно с CODING-RULES §1 (no snake_case).
- `<Subject>Should` — читается как "TotalCalculator should ReturnZeroForEmptyOrders".
- `DisplayName` — runner покажет осмысленное описание.

### DisplayName — Given / When / Then

Формат: **«Given `<precondition>`, when `<action>`, then `<expected>`»**.

```
[Fact(DisplayName = "Given user with admin role, when DELETE /api/orders/{id}, then returns 204")]

[Fact(DisplayName = "When Calculate is called with empty orders, then returns zero")]
```

`DisplayName` обязателен для integration и сложных unit. Для простых —
опционален.

---

## 3. `[Theory]` — DisplayName + InlineData

`DisplayName` описывает общий паттерн, конкретные значения подставит xUnit:

```csharp
[Theory(DisplayName = "Given two integers, when Add is called, then returns sum")]
[InlineData(0, 0, 0)]
[InlineData(1, 1, 2)]
[InlineData(-1, 1, 0)]
[InlineData(int.MaxValue, 1, int.MinValue)]
public void ReturnSum(int left, int right, int expected)
{
    var sum = Calculator.Add(left, right);

    sum.ShouldBe(expected);
}
```

**`[Fact]`** — один конкретный сценарий.
**`[Theory]` + `[InlineData]`** — один логический сценарий с разными входными.
**`[MemberData]`** — когда `InlineData` не хватает (объекты, коллекции):

```csharp
public static TheoryData<Order[], decimal> OrderTotalCases =>
[
    { [], 0m },
    { [new Order(100m, 1)], 100m },
    { [new Order(100m, 1), new Order(105m, 1)], 205m },
];

[Theory]
[MemberData(nameof(OrderTotalCases))]
public void ReturnCorrectTotal(Order[] orders, decimal expected)
{
    var total = new TotalCalculator().Calculate(orders);

    total.ShouldBe(expected);
}
```

`TheoryData<T1, T2, ...>` — типизированная альтернатива `IEnumerable<object[]>`.

---

## 4. Один Arrange, один Act, один Assert (концептуально)

«Один» не значит «одна строка». Значит — **одно действие** и **одно
утверждение о результате**. Можно проверять несколько свойств одного
объекта:

```csharp
// ✅ Один act, один логический assert
[Fact]
public void CreateOrderWithCorrectFields()
{
    var order = new OrderBuilder()
        .WithReference("ORD-100")
        .WithQuantity(1.5m)
        .Build();

    order.Reference.ShouldBe("ORD-100");
    order.Quantity.ShouldBe(1.5m);
    order.Status.ShouldBe(OrderStatus.New);
}

// ❌ Два разных act — нужно разбить на два теста
[Fact]
public void BuildOrders()
{
    var order1 = new OrderBuilder().WithReference("ORD-1").Build();
    order1.Reference.ShouldBe("ORD-1");

    var order2 = new OrderBuilder().WithQuantity(1m).Build();
    order2.Quantity.ShouldBe(1m);
}
```

---

## 5. Тестирование исключений

```csharp
// ✅ Через Shouldly
[Fact]
public void ThrowWhenOrdersIsNull()
{
    var calculator = new TotalCalculator();

    Should.Throw<ArgumentNullException>(() => calculator.Calculate(null!));
}

// ✅ Async версия
[Fact]
public async Task ThrowWhenIdIsEmpty()
{
    var service = new UserService();

    await Should.ThrowAsync<ArgumentException>(
        async () => await service.GetUserAsync(Guid.Empty));
}

// ✅ Проверка содержимого исключения
[Fact]
public void ThrowWithDescriptiveMessageForInvalidFormat()
{
    var parser = new SkuParser();

    var exception = Should.Throw<FormatException>(() => parser.Parse("BAD"));
    exception.Message.ShouldContain("Expected format: <CATEGORY>-<NUMBER>");
}
```

---

## 6. Assertions (Shouldly)

### Базовые ассерты

```csharp
// Equality
value.ShouldBe(expected);
value.ShouldNotBe(unexpected);

// Null
value.ShouldBeNull();
value.ShouldNotBeNull();

// Boolean
condition.ShouldBeTrue();
condition.ShouldBeFalse();

// Strings
text.ShouldStartWith("prefix");
text.ShouldEndWith("suffix");
text.ShouldContain("substring");
text.ShouldBeNullOrEmpty();
text.ShouldNotBeNullOrWhiteSpace();

// Numbers
amount.ShouldBeGreaterThan(0);
amount.ShouldBeInRange(0, 100);
amount.ShouldBeNegative();

// Collections
items.ShouldBeEmpty();
items.ShouldNotBeEmpty();
items.ShouldHaveSingleItem();
items.Count.ShouldBe(3);
items.ShouldContain(item);
items.ShouldAllBe(item => item.IsValid);
items.ShouldBeInOrder();

// Types
result.ShouldBeOfType<SuccessResult>();
result.ShouldBeAssignableTo<IResult>();

// Exceptions
Should.Throw<ArgumentException>(() => Action());
await Should.ThrowAsync<HttpRequestException>(async () => await Action());

// Reference equality
actual.ShouldBeSameAs(expected);
actual.ShouldNotBeSameAs(other);
```

### Цепочки и сложные проверки

Для проверки нескольких свойств — отдельными ассертами:

```csharp
// ✅ Каждое свойство — отдельная строка
order.ShouldNotBeNull();
order.Reference.ShouldBe("ORD-100");
order.Quantity.ShouldBe(1.5m);
order.Status.ShouldBe(OrderStatus.New);

// ❌ Не делаем так — теряем имя при ошибке
order.ShouldSatisfyAllConditions(
    () => order.Reference.ShouldBe("ORD-100"),
    () => order.Quantity.ShouldBe(1.5m)
);
```

`ShouldSatisfyAllConditions` оправдан когда **важно увидеть все провалившиеся
ассерты сразу**, а не остановиться на первом.

### `Should()` (legacy FluentAssertions API) — ЗАПРЕЩЁН

```csharp
// ✅ Shouldly
result.ShouldBe(42);

// ❌ FluentAssertions API
result.Should().Be(42);
```

---

## 7. Mocking (NSubstitute)

### Когда мокаем

Моки — для **внешних** зависимостей, которые невозможно или дорого
поднять реально:
- Внешние HTTP API (каталог, платёжный провайдер) в unit-тестах логики.
- Time providers (`IClock`, `TimeProvider`) — детерминированное тестирование.
- Message bus producers — проверяем, что событие было опубликовано.

**Не мокаем:**
- DbContext / репозитории — для них integration с Testcontainers.
- `IOptions<T>` / `IConfiguration` — создаём руками.
- Логгеры (`ILogger<T>`) — `NullLogger<T>.Instance`.

### Базовый синтаксис

```csharp
[Fact]
public async Task CallCatalogWithCorrectPayload()
{
    var catalogClient = Substitute.For<ICatalogClient>();
    catalogClient.PlaceOrderAsync(Arg.Any<OrderRequest>())
        .Returns(new OrderResponse { Id = "ORD-123", Status = "Placed" });

    var processor = new OrderProcessor(catalogClient);

    var result = await processor.ProcessAsync(new Order("ORD-100", 1m));

    result.ExternalId.ShouldBe("ORD-123");
    await catalogClient.Received(1).PlaceOrderAsync(
        Arg.Is<OrderRequest>(request => request.Reference == "ORD-100"));
}
```

### Arg matchers

```csharp
Arg.Any<Order>()
Arg.Any<string>()
Arg.Any<CancellationToken>()

Arg.Is<int>(value => value > 0)
Arg.Is<Order>(order => order.Reference == "ORD-100")
Arg.Is(42)
```

### Returns vs Throws

```csharp
mock.GetAsync(Arg.Any<Guid>()).Returns(user);

mock.GetAsync(Arg.Any<Guid>())
    .Returns(callInfo => new User { Id = callInfo.Arg<Guid>() });

mock.GetAsync(Arg.Any<Guid>()).Throws(new InvalidOperationException());

mock.GetAsync(Arg.Any<Guid>()).ThrowsAsync(new HttpRequestException("503"));
```

### Verify взаимодействия

```csharp
mock.Received().Method(Arg.Any<int>());
mock.Received(3).Method(Arg.Any<int>());
mock.DidNotReceive().Method(Arg.Any<int>());
await mock.Received(1).MethodAsync(Arg.Any<int>());
```

### Что НЕ делаем с моками

```csharp
// ❌ Мок на класс, который сами написали и можем создать
var calculator = Substitute.For<ITotalCalculator>();
calculator.Calculate(Arg.Any<Order[]>()).Returns(5m);
// Используй настоящий TotalCalculator если он без I/O

// ❌ Несколько моков для проверки одной интеграции
var clientA = Substitute.For<IClientA>();
var clientB = Substitute.For<IClientB>();
var clientC = Substitute.For<IClientC>();
// Признак того, что нужен integration тест.
```

---

## 8. Test data — Builder / Bogus

### Object Mother / Builder для сложных объектов

Если объект имеет 5+ полей и используется в 3+ тестах — выноси в Builder:

```csharp
public sealed class OrderBuilder
{
    private string reference = "ORD-100";
    private decimal quantity = 1m;
    private decimal price = 100m;
    private OrderStatus status = OrderStatus.New;

    public OrderBuilder WithReference(string value) { reference = value; return this; }
    public OrderBuilder WithQuantity(decimal value) { quantity = value; return this; }
    public OrderBuilder WithPrice(decimal value) { price = value; return this; }
    public OrderBuilder WithStatus(OrderStatus value) { status = value; return this; }

    public Order Build() => new() { Reference = reference, Quantity = quantity, Price = price, Status = status };
}
```

Builders живут в `Builders/` папке тест-проекта, не в production-коде.

### Bogus для массовой генерации

```csharp
var faker = new Faker<User>()
    .RuleFor(user => user.Id, faker => Guid.NewGuid())
    .RuleFor(user => user.Email, faker => faker.Internet.Email())
    .RuleFor(user => user.CreatedAt, faker => faker.Date.Past());

var users = faker.Generate(100);
```

Фиксированный seed для воспроизводимости:

```csharp
.UseSeed(42)
```

---

## 9. Anti-patterns

### Тесты, которые тестируют моки

```csharp
// ❌ Это тест что NSubstitute работает, а не наш код
var repository = Substitute.For<IUserRepository>();
repository.GetByIdAsync(Arg.Any<Guid>()).Returns(new User { Id = id });

var result = await repository.GetByIdAsync(id);

result.Id.ShouldBe(id);
```

### Один тест на много кейсов через if/switch

```csharp
// ❌ Сложный тест, непонятно что упало
[Fact]
public void HandleAllOrderTypes()
{
    foreach (var orderType in Enum.GetValues<OrderType>())
    {
        var result = processor.Process(orderType);

        if (orderType == OrderType.Standard) result.ShouldNotBeNull();
        else if (orderType == OrderType.Express) result.Price.ShouldBeGreaterThan(0);
    }
}

// ✅ Theory с InlineData
[Theory]
[InlineData(OrderType.Standard)]
[InlineData(OrderType.Express)]
[InlineData(OrderType.Gift)]
public void ReturnNonNullResult(OrderType orderType)
{
    var result = processor.Process(orderType);

    result.ShouldNotBeNull();
}
```

### Магические числа без объяснения

```csharp
// ❌ Что за 19.99?
total.ShouldBe(19.99m);

// ✅ Константа с именем
const decimal ExpectedOrderTotal = 19.99m;
total.ShouldBe(ExpectedOrderTotal);

// ✅ Или комментарий
// 19.99 — типичный total заказа из одного product'а со стандартной скидкой 10%
total.ShouldBe(19.99m);
```

### Излишний setup / teardown

```csharp
// ❌ Глобальный setup
public sealed class CalculatorShould : IDisposable
{
    private readonly Calculator calculator;
    private readonly List<int> testData;
    private readonly Mock<ILogger> logger;

    public CalculatorTests()
    {
        calculator = new Calculator();
        testData = new List<int> { 1, 2, 3 };
        logger = new Mock<ILogger>();
    }

    [Fact]
    public void ReturnCorrectSum()
    {
        var result = calculator.Add(2, 3);  // testData и logger не нужны
        result.ShouldBe(5);
    }
}

// ✅ Создаём что нужно прямо в тесте
public sealed class CalculatorShould
{
    [Fact]
    public void ReturnCorrectSum()
    {
        var result = new Calculator().Add(2, 3);

        result.ShouldBe(5);
    }
}
```

### Тестирование private методов

```csharp
// ❌ Reflection до private — sign что архитектура неправильная
typeof(Calculator)
    .GetMethod("ComputeInternal", BindingFlags.NonPublic | BindingFlags.Instance)!
    .Invoke(calculator, [1, 2]);

// ✅ Либо тестируем через public, либо метод вынесен в отдельный класс.
```

### `Task.Delay` для ожидания async операций

```csharp
// ❌ Flaky
worker.Start();
await Task.Delay(1000);
worker.ProcessedCount.ShouldBeGreaterThan(0);

// ✅ Polling с timeout
worker.Start();
await WaitForAsync(() => worker.ProcessedCount > 0, TimeSpan.FromSeconds(5));
```

### Игнорирование/skipping тестов без TODO

```csharp
// ❌ Без объяснения когда вернёмся
[Fact(Skip = "Broken")]
public void Test() { ... }

// ✅ С контекстом и ссылкой
[Fact(Skip = "Flaky — gh-issue #1234, recheck after Q2 refactor")]
public void Test() { ... }
```

---

## Связанные правила

- `testing-stack-and-pyramid.md` — stack, decision tree
- `testing-integration.md` — integration tests (reference)
- `project-deps-and-tests.md` — testing structure