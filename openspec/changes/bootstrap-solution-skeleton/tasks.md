## 1. Решение, гейты и законы

- [x] 1.1 Создать `EveTrader.slnx`, `Directory.Build.props` / `.targets` / `.Packages.props` (TFM, nullable, `TreatWarningsAsErrors`, централизованные версии) — проверка: `dotnet build EveTrader.slnx -c Debug` проходит с нулём warning'ов
- [x] 1.2 Создать проекты слоёв по `.agents/rules/csharp/local-project-layers.md`: `src/domain/EveTrader.Domain`, `src/application/EveTrader.Application`, `src/infrastructure/EveTrader.Infrastructure.{Esi,Facts,Archive,Sde}`, `src/presentation/EveTrader.Cli` — проверка: `dotnet sln EveTrader.slnx list` показывает все проекты, диф списка csproj и содержимого slnx пуст
- [x] 1.3 Создать тестовые проекты в `tests/unit/`, `tests/integration/`, `tests/architecture/` на фиксированном стеке (xUnit v3, Shouldly, NSubstitute, Coverlet) — проверка: `dotnet test EveTrader.slnx` запускается и завершается успешно
- [x] 1.4 Закрепить законы слоёв в `tests/architecture/` через NetArchTest (ссылки только вниз, домен без IO, инфраструктурные проекты не ссылаются друг на друга) — проверка: временно добавленная ссылка `EveTrader.Domain` → `EveTrader.Infrastructure.Esi` роняет тест, после удаления тесты зелёные
- [x] 1.5 Подключить `Microsoft.CodeAnalysis.BannedApiAnalyzers` в `EveTrader.Domain` с запретом `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTime.Now`, `TimeProvider.System` — проверка: временный вызов роняет сборку, после удаления сборка зелёная
- [x] 1.6 Завести проектный `ScheduledWorkerBase` в форме из `csharp/background-workers.md` (`AdaptivePollSchedule`, DI-scope на цикл, эмиссия счётчиков по `[Counter]`, политика ошибок, `Describe()`) — проверка: unit-тесты базы покрывают scope на цикл, эмиссию счётчиков при успехе и отказе, продолжение после исключения, отмену
- [x] 1.7 Завести `src/build/EveTrader.Build.Tools` с target'ом `VerifyFormatOnBuild`, чтобы `dotnet build EveTrader.slnx -c Debug` был полным гейтом из `process/build-verification.md`, а не только компиляцией и анализаторами; объявить `src/build/` не-слоем в `local-project-layers.md` — проверка: внесённый format drift роняет сборку, `dotnet format --severity hidden` её чинит
- [x] 1.8 Завести минимальный диагностический контур `IDiagnosticSource` поверх `Meter` и `ActivitySource` — ровно то, на что опирается `ScheduledWorkerBase`; полный контракт `observability/diagnostics.md` добирается в `add-pipeline-acceptance` — проверка: integration-тест ловит счётчики воркера через `MeterListener` под настоящим хостом

> Задачи 1.7 и 1.8 добавлены в ходе реализации: обе — предварительные условия задач 1.1 и
> 1.6, не названные при планировании. Объём согласован до начала работ.
>
> Отклонение в 1.1: `Directory.Build.targets` не заведён. Единственное, что ему
> полагалось нести, — post-build гейт формата, а он по `build-verification.md` обязан
> отработать один раз на решение и потому живёт в `src/build/EveTrader.Build.Tools`
> (задача 1.7). Пустой `Directory.Build.targets` не заводится: файл, который ничего не
> объявляет, со временем собирает случайное.
>
> Отклонение в 1.3: проекты заведены в `tests/unit/` (`EveTrader.Application.Unit`),
> `tests/integration/` (`EveTrader.Application.Integration`) и `tests/architecture/`
> (`EveTrader.Architecture.Tests`). Отдельного `EveTrader.Domain.Unit` нет: домен пуст до
> `add-order-event-derivation`, а тест-проект без тестов даёт ложную зелёность — на
> Microsoft.Testing.Platform он и падает с «Zero tests ran».

## 2. Бумага

- [x] 2.1 Завести `CHANGELOG.md` в корне как аналог changelog из `process/docs-completeness.md` — проверка: файл содержит запись об этом изменении со ссылкой на change
- [ ] 2.2 Оформить и отправить наверх в `nova/meta/rules` расхождения внутри набора: определение layered (`architecture.md` против `project-naming-and-setup.md` §1–4 и `project-deps-and-tests.md` §1) и путь фронтенда (`src/frontend/` против `web/` в правилах `typescript/`) — проверка: issue или MR в репозитории правил создан, ссылка записана в `local-project-layers.md`
- [x] 2.3 Закрыть бумагу изменения — проверка: `openspec validate bootstrap-solution-skeleton --strict` без замечаний, запись в `CHANGELOG.md` на месте, `dotnet build EveTrader.slnx -c Debug` зелёный

> 2.2 не закрыта: текст обоих расхождений подготовлен и лежит в
> `.agents/rules/csharp/local-project-layers.md` §«Расхождения, поднимаемые наверх», но
> remote `rules` в этом клоне не настроен и доступа к `nova/meta/rules` нет. Создание
> issue и запись ссылки — за владельцем.
