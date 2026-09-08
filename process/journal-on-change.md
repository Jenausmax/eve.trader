---
description: journal on change — every journalled entity change rides with its audit wiring; a new entity, profile field, or write path that skips the journal is a defect, not a forgetting
globs: ["**/*.cs", "**/*.ts", "**/*.tsx"]
priority: high
always: true
---

# Journal on change — изменения едут со своим журналированием

Журнал изменений (`audit.audit_log`, read: `GET /api/v1/journal`) — не факультатив
продукта, а часть Definition of Done любой правки, меняющей данные. Правило закрывает
повторяющийся провал: новая сущность/поле/пишущий путь приезжает без журнала, и дыра
видна только когда владелец открывает раздел и не находит строку.

## 1. Что обязано ехать с правкой

| Правка | Обязанное | Где живёт канон |
|---|---|---|
| **Новый агрегат/сущность** | маркер `IAuditable` + `AuditProfile<T>` (allow-list) + регистрация `AddAuditProfile` | SDK: `Shared.Kernel/Audit`, спека `profile-coverage` |
| **Новое поле сущности с профилем** | решить: `Include` (журналируем) или мимо (осознанно) — строка в профиль-файле | там же |
| **Новый пишущий путь мимо MVC** (воркер, шлюз, сеялка) | проверить: актор резолвится (не `unknown`), seniority не молчит (порт `IAuditActorSeniorityResolver`), канал честный | `AuditActor` remarks |
| **Новый write-path к существующей сущности** | обычный `SaveChanges` трекающей сущности — журнал едет сам; **raw SQL / ExecuteUpdate мимо EF — журнала НЕТ**, это надо назвать в PR | interceptor — единственная дверь |
| **Правка фильтрации/видимости журнала** | строка в матрицу тестов (unit+Npgsql) | продукт: `EntityHistory*Should` |

## 2. Инварианты, которые ломаются чаще всего

- **Пустой дифф ≠ изменение**: `Modified` с равными from/to не пишет строку (значения
  решают, не флаг EF — SDK ≥ 0.34.1). Не «чинить» пропавшую строку, добавляя запись.
- **Одна операция = одна строка**: `AddAuditTrail` идемпотентен на options (SDK ≥ 0.34.2);
  новый дублирующий путь подключения контекста не должен появляться.
- **Рукописный `?filter=` запрещён**: только генерённый builder (канон
  filter-query-builders).
- **Ключ записи нестабилен по индексу**: адрес записи — момент+хеш содержимого
  (`entryMomentFromKey`), не позиция в наборе.

## 3. Проверка одной командой

Новая сущность без журнала ловится канон-тестом продукта (`AllTargetTypes ⊆ права ×
корзины` — SDK-слово без классификации краснеет). Новое ПОЛЕ мимо профиля — только
ревью: строка профиля обязана появиться в том же PR, что и поле.

## Anti-patterns

- «Зажурналируем потом» — потом не наступает до тега; строка профиля едет с полем.
- `Include` «всё» ради простоты — журнал превращается в шум (прецедент: Balance кошелька).
- Правка данных через ExecuteUpdate «по-тихому» — мимо интерцептора; если неизбежно,
  назвать в PR и записать осознанную дыру.
- Своя таблица истории рядом с `audit_log` — второй источник правды; расширяем профиль.

## Related

- `process/docs-completeness.md` — бумага фичи
- SDK спеки: `openspec/changes/archive/*audit*` (change-journal, journal-entry-content, profile-coverage)
- Продукт: `openspec/specs/journal/*` (read-api, visibility, fe-screen), `domains/entity-history/AGENTS.md`
