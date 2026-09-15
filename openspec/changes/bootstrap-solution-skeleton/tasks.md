## 1. Решение, гейты и законы

- [ ] 1.1 Создать `EveTrader.slnx`, `Directory.Build.props` / `.targets` / `.Packages.props` (TFM, nullable, `TreatWarningsAsErrors`, централизованные версии) — проверка: `dotnet build EveTrader.slnx -c Debug` проходит с нулём warning'ов
- [ ] 1.2 Создать проекты слоёв по `.agents/rules/csharp/local-project-layers.md`: `src/domain/EveTrader.Domain`, `src/application/EveTrader.Application`, `src/infrastructure/EveTrader.Infrastructure.{Esi,Facts,Archive,Sde}`, `src/presentation/EveTrader.Cli` — проверка: `dotnet sln EveTrader.slnx list` показывает все проекты, диф списка csproj и содержимого slnx пуст
- [ ] 1.3 Создать тестовые проекты в `tests/unit/`, `tests/integration/`, `tests/architecture/` на фиксированном стеке (xUnit v3, Shouldly, NSubstitute, Coverlet) — проверка: `dotnet test EveTrader.slnx` запускается и завершается успешно
- [ ] 1.4 Закрепить законы слоёв в `tests/architecture/` через NetArchTest (ссылки только вниз, домен без IO, инфраструктурные проекты не ссылаются друг на друга) — проверка: временно добавленная ссылка `EveTrader.Domain` → `EveTrader.Infrastructure.Esi` роняет тест, после удаления тесты зелёные
- [ ] 1.5 Подключить `Microsoft.CodeAnalysis.BannedApiAnalyzers` в `EveTrader.Domain` с запретом `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTime.Now`, `TimeProvider.System` — проверка: временный вызов роняет сборку, после удаления сборка зелёная
- [ ] 1.6 Завести проектный `ScheduledWorkerBase` в форме из `csharp/background-workers.md` (`AdaptivePollSchedule`, DI-scope на цикл, эмиссия счётчиков по `[Counter]`, политика ошибок, `Describe()`) — проверка: unit-тесты базы покрывают scope на цикл, эмиссию счётчиков при успехе и отказе, продолжение после исключения, отмену

## 2. Бумага

- [ ] 2.1 Завести `CHANGELOG.md` в корне как аналог changelog из `process/docs-completeness.md` — проверка: файл содержит запись об этом изменении со ссылкой на change
- [ ] 2.2 Оформить и отправить наверх в `nova/meta/rules` расхождения внутри набора: определение layered (`architecture.md` против `project-naming-and-setup.md` §1–4 и `project-deps-and-tests.md` §1) и путь фронтенда (`src/frontend/` против `web/` в правилах `typescript/`) — проверка: issue или MR в репозитории правил создан, ссылка записана в `local-project-layers.md`
- [ ] 2.3 Закрыть бумагу изменения — проверка: `openspec validate bootstrap-solution-skeleton --strict` без замечаний, запись в `CHANGELOG.md` на месте, `dotnet build EveTrader.slnx -c Debug` зелёный
