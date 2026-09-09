---
description: local declarations of the eve.trader repository — project tag, docs language, rules update flow
priority: high
always: true
---

# eve.trader — локальные объявления

Файл закрывает то, что общие правила требуют объявить у потребителя.
Общие файлы набора правятся **только** в `nova/meta/rules`.

## Тег проекта в коммитах

Формат из [`commit-format.md`](commit-format.md) с подставленным тегом:

```
[eve.trader](feat/<area>): <subject>
```

Каталог областей (`feat/<area>`) отдельным файлом **не зафиксирован** —
структура решения ещё не сложилась. Пока области выбираются по месту, по правилу
из `commit-format.md`; безопасный дефолт для правил, сборки, CI и скриптов —
`feat/meta`, для документации — `feat/docs`. Когда появятся модули, каталог
выносится в `local-commit-format-areas.md`.

## Язык документации

Русский. Касается ADR, README, журналов изменений и комментариев в коде,
где они уместны. Идентификаторы, имена типов и коммит-subject — английский.

## Инженерная зона

Своего списка нет — действует дефолт из
[`engineering-zone-access.md`](engineering-zone-access.md) §«What "engineering
zone" means»: `EveTrader.slnx`, `Directory.*.props`/`.targets`, `.editorconfig`,
composition root, CI, `.agents/rules/**`, `CLAUDE.md`. Свой список появится
вместе со слоями решения.

## Процесс — OpenSpec

Разработка spec-driven через OpenSpec: норма поведения продукта — `openspec/specs/`,
изменения — `openspec/changes/`, проектный контекст для артефактов — блок `context`
в `openspec/config.yaml`. Артефакты на русском.

Спеки описывают **что делает система**, этот набор правил — **как писать код**.
Спека, пересказывающая правило, и правило, описывающее поведение продукта, —
оба дубли; см. `CLAUDE.md` §«Процесс разработки — OpenSpec».

## Правила, которые здесь не применяются

Два правила помечены `always: true`, но написаны под другой проект и в этом
репозитории неисполнимы. Объявляю это здесь, чтобы их не пытались исполнить буквально
и не изобретали отсутствующие механизмы.

| Правило | Почему не применяется |
|---|---|
| `process/product-platform-pair.md` | описывает пару репозиториев «продукт + платформа» с публикацией пакетов по тегу. eve.trader — один репозиторий, платформы-соседа нет |
| `process/journal-on-change.md` | описывает журнал аудита (`audit.audit_log`, `IAuditable`, `AuditProfile<T>`) поверх EF в продукте с пользовательскими правками данных. Здесь нет ни EF над рыночными фактами, ни изменяемых пользователем сущностей: факты иммутабельны, а происхождение каждого несут `observed_at` / `as_of` и журнал покрытия — см. §«Граница персистентности» в `csharp/local-project-layers.md` |

Появится операционный контур с изменяемыми сущностями — `journal-on-change.md`
начнёт применяться к нему, и эта строка сузится до рыночных фактов.

## Бумага изменения

`process/docs-completeness.md` требует три артефакта; для этого репозитория они такие:

| Артефакт | Здесь |
|---|---|
| OpenSpec change | `openspec/changes/<slug>/` — как в правиле |
| Запись в changelog | `CHANGELOG.md` в корне (аналог `deploy/docs/pages/changelog.md` из правила) |
| Запись в `STATE.md` | **не ведётся** — фазовой раскладки в проекте нет, состояние работ несёт сам OpenSpec |

## Обновление общего набора

```bash
git subtree pull --prefix=.agents/rules rules master --squash
```

Remote `rules` → `git@gitlab.hybrid.ai:nova/meta/rules.git`, добавлен локально
(`git remote add`), в репозитории не хранится — после свежего `clone` его надо
добавить заново.

Дрейф локальных правок в общих файлах ловится манифестом:

```bash
node .agents/rules/scripts/manifest.mjs
```

> На момент подключения (коммит `1419a30`) манифест наверху расходится с
> содержимым по `process/docs-completeness.md` и `process/journal-on-change.md`
> — это несгенерированный `MANIFEST.json` **в самом наборе**, не наша правка.
> Лечится в `nova/meta/rules` через `node scripts/manifest.mjs --write`.
