## Why

Репозиторий пустой: ни решения, ни проектов, ни гейтов. Любой следующий чейндж конвейера
наблюдения начинается с одного и того же вопроса «куда класть код», и без ответа каждый
из них заводил бы раскладку заново — по-своему.

Отдельный чейндж под скелет нужен ещё и потому, что **законы, которые дешевле всего
закрепить до первой строки кода, невозможно закрепить после**. Запрет системных часов в
домене, направление ссылок между слоями, ноль warning'ов — всё это вводится в пустом
решении бесплатно, а в решении с тридцатью файлами превращается в отдельную работу по
разгребанию.

Поведения продукта здесь нет: чейндж не добавляет и не меняет ни одного требования в
`openspec/specs/`, поэтому объявлен `skip_specs: true`. Это строительные леса, и
проверяются они сборкой, а не сценариями.

## What Changes

- **Решение и централизованная сборка.** `EveTrader.slnx`, `Directory.Build.props` /
  `.targets` / `.Packages.props`: TFM, nullable, `TreatWarningsAsErrors`, версии пакетов
  пинуются в одном месте.
- **Проекты слоёв** по `.agents/rules/csharp/local-project-layers.md`: `src/domain`,
  `src/application`, `src/infrastructure/{Esi,Facts,Archive,Sde}`,
  `src/presentation/EveTrader.Cli`.
- **Тестовые проекты** в `tests/unit/`, `tests/integration/`, `tests/architecture/` на
  фиксированном стеке из `csharp/testing-stack-and-pyramid.md` (xUnit v3, Shouldly,
  NSubstitute, NetArchTest.Rules, Coverlet).
- **Законы слоёв исполняемы.** NetArchTest закрепляет: ссылки только вниз, домен без IO,
  инфраструктурные проекты не ссылаются друг на друга.
- **Запрет «сейчас» в домене — анализатором, а не тестом.**
  `Microsoft.CodeAnalysis.BannedApiAnalyzers` банит `DateTime.UtcNow`,
  `DateTimeOffset.Now`, `DateTime.Now`, `TimeProvider.System` в доменном проекте.
  NetArchTest для этого не годится: он смотрит на зависимости типов, а не на вызовы.
- **`ScheduledWorkerBase`** в форме из `csharp/background-workers.md`:
  `AdaptivePollSchedule`, DI-scope на цикл, счётчики через `[Counter]`, политика ошибок,
  `Describe()`. Первый периодический воркер приедет в `add-live-hub-collection`, но база
  под него заводится здесь, чтобы тот чейндж не смешивал базу с логикой сбора.
- **`CHANGELOG.md`** в корне как аналог changelog из `process/docs-completeness.md`.
- **Расхождения внутри набора правил поднимаются наверх** в `nova/meta/rules` — см.
  §Impact.

**Non-goals.** Никакой доменной логики, никаких обращений к ESI, никакого хранилища.
Проекты создаются пустыми или с минимальным содержимым, достаточным, чтобы гейты имели
что проверять.

## Capabilities

Изменение не затрагивает норм поведения продукта — `skip_specs: true`. Поведение приедет
следующими чейнджами конвейера, начиная с `add-bitemporal-fact-lake`.

## Impact

- **Greenfield.** Внешних потребителей нет, миграции нет, откат — удаление файлов.
- **Стиль архитектуры объявлен layered** в `.agents/rules/csharp/local-project-layers.md`
  (требование `csharp/architecture.md`).
- **Отклонение от раскладки правил, зафиксированное осознанно.** `csharp/architecture.md`
  описывает layered как `presentation → application → domain ← infrastructure`, а
  `csharp/project-naming-and-setup.md` §1–4 и `csharp/project-deps-and-tests.md` §1 — как
  сервисную раскладку `application/ feature/ database/ client/ models/ shared/`. Оба
  правила `always: true` и расходятся между собой. Разбор — в `design.md`.
- **Правила EF** (`csharp/ef-core.md`, `csharp/entity-models.md`, оба `always: true`)
  относятся к операционному контуру и не распространяются на рыночные факты — объявлено
  в `local-project-layers.md`. `DbContext` в этом чейндже не появляется.
- **Регистрация в `EveTrader.slnx` обязательна** для каждого нового `.csproj` —
  `process/project-slnx-registration.md`.
- **Зависимости**, пинуемые здесь централизованно: `Parquet.Net`,
  `DuckDB.NET.Data.Full`, `Microsoft.CodeAnalysis.BannedApiAnalyzers`, тестовый стек.
  Ссылки на них появятся в последующих чейнджах, версии объявляются сразу.

## Предварительные условия

Нет — это первый чейндж репозитория. Всё остальное зависит от него.
