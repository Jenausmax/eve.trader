# eve.trader

Агент-трейдер для EVE Online: собирает и анализирует рыночные и исторические
данные по продажам, выдаёт рекомендации и алерты.

Стек — **.NET 10**. Доступ к игре — только read-only публичный ESI (SSO и запись
закладываются в архитектуру, но не включены). ESI не имеет эндпоинтов
создания/изменения маркет-ордеров: автоматизация клиента нарушает EULA, поэтому
потолок агента — чтение, рекомендации, алерты.

## Процесс разработки — OpenSpec

Разработка идёт spec-driven через [OpenSpec](https://github.com/Fission-AI/OpenSpec)
(CLI `openspec`, схема `spec-driven`). Норма поведения продукта живёт в
[`openspec/specs/`](openspec/) — требование, записанное вне неё, требованием не
является. Изменения въезжают через предложения в `openspec/changes/`.

| Команда | Когда |
|---|---|
| `/opsx:explore` | разобраться в области до предложения |
| `/opsx:propose` | новое предложение изменения |
| `/opsx:update` | правка предложения |
| `/opsx:apply` | реализация по принятому предложению |
| `/opsx:archive` | завершённое изменение → обновление норм в `specs/` |
| `/opsx:sync` | сверка спек с кодом |

```bash
openspec list            # изменения в работе
openspec list --specs    # нормы
openspec validate        # проверить артефакты
openspec status          # готовность артефактов изменения
```

Проектный контекст для генерации артефактов — блок `context` в
[`openspec/config.yaml`](openspec/config.yaml): там же зафиксированы внешние
ограничения ESI, чтобы они не всплывали заново в каждом предложении.

**Разделение с правилами:** `openspec/specs/` отвечает на «что система делает»,
`.agents/rules/` — на «как писать код». Спеки не пересказывают правила, правила
не описывают поведение продукта.

## Правила

Канон живёт в [`.agents/rules/`](.agents/rules/) — общий набор владельца,
подключён git subtree из `nova/meta/rules`. Читать оттуда, а не по памяти.

| Раздел | Про что |
|---|---|
| [`process/`](.agents/rules/process/) | режим работы агента: коммиты, build gate, self-audit, worktree, секреты, инженерная зона |
| [`csharp/`](.agents/rules/csharp/) | C#: архитектура, нейминг, async, API, EF, тесты, структура проектов |
| [`typescript/`](.agents/rules/typescript/) | фронтенд — про запас, пока не используется |
| [`observability/`](.agents/rules/observability/) | OTel-контракт |

Карта правил (иерархия, frontmatter, куда класть новое):
[`csharp/rules-format.md`](.agents/rules/csharp/rules-format.md).
Локальные объявления этого репозитория:
[`process/local-project.md`](.agents/rules/process/local-project.md).
Указатели для batch-агентов — [`.claude/rules/`](.claude/rules/).

### Что действует всегда

- **Коммит** — `[eve.trader](feat/<area>): <subject>`, см.
  [`commit-format.md`](.agents/rules/process/commit-format.md).
- **Build gate** — [`build-verification.md`](.agents/rules/process/build-verification.md).
- **Self-audit** — [`worker-audit.md`](.agents/rules/process/worker-audit.md).
- **Инженерная зона** — [`engineering-zone-access.md`](.agents/rules/process/engineering-zone-access.md).
- **Новый `.csproj`** — регистрация в `EveTrader.slnx` обязательна,
  см. [`project-slnx-registration.md`](.agents/rules/process/project-slnx-registration.md).
- **Секреты** — [`secrets.md`](.agents/rules/process/secrets.md), в репозиторий не попадают.

### Правка правил

Общий файл (`csharp/`, `typescript/`, `process/`, `observability/` без префикса
`local-`) правится **в `nova/meta/rules`**, не здесь: следующий `subtree pull`
затрёт правку на месте. Локальное правило кладётся рядом с префиксом `local-`.

Язык документации — русский.
