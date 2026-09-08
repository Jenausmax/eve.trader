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
