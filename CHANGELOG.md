# Changelog

Журнал изменений eve.trader. Запись появляется вместе с изменением, а не после тега —
`process/docs-completeness.md`. Каждая запись ссылается на свой OpenSpec change.

Формат: обратный хронологический порядок, новое сверху.

## Не выпущено

### Скелет решения и гейты

Change: [`bootstrap-solution-skeleton`](openspec/changes/bootstrap-solution-skeleton/)

- Заведено решение `EveTrader.slnx` и централизованная сборка: `Directory.Build.props`,
  `Directory.Build.targets`, `Directory.Packages.props`, `global.json` (SDK 10.0.401),
  `.editorconfig`.
- Созданы проекты слоёв по `csharp/local-project-layers.md`: `EveTrader.Domain`,
  `EveTrader.Application`, `EveTrader.Infrastructure.{Esi,Facts,Archive,Sde}`,
  `EveTrader.Cli`.
- Гейт сборки — одна команда `dotnet build EveTrader.slnx -c Debug`: компиляция,
  анализаторы и проверка форматирования через `EveTrader.Build.Tools`.
- Законы слоёв исполняемы: `tests/architecture/` проверяет направление ссылок и запрет
  ссылок между инфраструктурными проектами по графу `ProjectReference`, NetArchTest —
  зависимости типов домена.
- Запрет системных часов в домене закреплён `BannedApiAnalyzers`: `DateTime.UtcNow`,
  `DateTime.Now`, `DateTimeOffset.UtcNow`, `DateTimeOffset.Now`, `TimeProvider.System`
  роняют сборку.
- Заведён `ScheduledWorkerBase` с расписаниями `IntervalSchedule`,
  `AdaptivePollSchedule`, `StartupSchedule`, DI-scope на цикл и эмиссией счётчиков по
  `[Counter]`; под него — минимальный диагностический контур `IDiagnosticSource` поверх
  `Meter` и `ActivitySource`.
- Тестовый стек зафиксирован: xUnit v3 на Microsoft.Testing.Platform, Shouldly,
  NSubstitute, NetArchTest.Rules, Coverlet.
