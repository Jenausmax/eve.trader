---
description: secrets handling — never print, log, or commit secrets; redaction; secret-store layout
priority: high
always: true
---

# Secrets handling rule (global)

> Это правило всегда в контексте — оно автоматически подгружается во
> все pi-сессии (глобальный user rule в `~/.agents/rules/`).
> Детальные инструкции и скрипты — в skill `/skill:secrets`.

## Где живут секреты пользователя

Store partitioned **by project, not by credential type**. One folder per
project; inside a project, files sit side-by-side regardless of type (.env
token, .yaml account meta, .json oauth, kubeconfig .yaml).

```
~/.secrets/                                # user-global, not tied to any agent
├── <project>/                         # одна папка на проект
│   ├── <service>.env                  # KEY=value, source-able, 0600
│   ├── <service>.yaml                 # login/url/notes, БЕЗ паролей
│   ├── <service>.json                 # oauth access/refresh/expiry, 0600
│   └── <service>-cluster.yaml         # kubeconfig / cluster access, 0600
├── personal/                          # cross-project / user-global (github PAT,
│                                      #   openai, tracker, vpn, …)
├── ssh/<name>/{id_<name>, id_<name>.pub, meta.yaml}   # GLOBAL — machine identity
└── keys/<env|project>/…               # legacy one-file-per-key k8s dump (special)
```

**Project = a top-level dir that is NOT one of the reserved globals**
(`ssh`, `keys`, `personal`). `personal/` is the "no-project" bucket for
creds that don't belong to one project (personal gitlab.com PAT, personal
VPN, etc.).

**Naming.** `<project>`, `<service>`, `<name>` — `[a-z0-9-]+` (lowercase,
digits, hyphen). A credential is addressed as `<project>/<service>` (e.g.
`<project>/minio-dev`, `<project>/proxmox`); the skill scripts take this as
their first argument. The `.env` and `.yaml` for the same service share a stem
(`<project>/minio-dev.env` + `<project>/minio-dev.yaml`).

**Why project-first.** Credential lifecycle is bounded by the project
(one project's infra rotates together); type-splitting
(`tokens/` vs `accounts/` vs `oauth/`) scattered one logical credential
across three folders and made per-project audit impossible. SSH keys stay
global because they are machine identity reused across projects, not a
service credential.

> **Migration history:**
> - До 2026-07-20 секреты жили в `~/.pi/secrets/`. Store переехал в
>   `~/.secrets/` чтобы не путать с `pi`-agent namespace.
> - До 2026-07-29 store был type-first (`tokens/`, `accounts/`, `oauth/`).
>   2026-07-29 переехал в project-first (`<project>/`, `personal/`).
>   `ssh/` и `keys/` оставлены как специальные глобальные разделы.
> - Прямая ссылка на `~/.pi/secrets/` ИЛИ на `~/.secrets/{tokens,accounts,oauth}/`
>   в любом месте — баг, репорти.

## Что ты (агент) МОЖЕШЬ

1. **Читать метаданные.** `ls ~/.secrets/`, `find ~/.secrets/...`,
   `cat ~/.secrets/ssh/<name>/meta.yaml`,
   `cat ~/.secrets/<project>/<service>.yaml`.
2. **Создавать новые секреты.** Запускать `~/.pi/agent/skills/secrets/scripts/*.sh`,
   `ssh-keygen`, писать шаблоны env/yaml/json с placeholder.
3. **Читать публичные ключи.** `*.pub` — это публичные данные,
   их можно смело показывать в чате и копировать на github/gitlab.
4. **Инструктировать пользователя** командами для самостоятельного просмотра
   (`cat ...`, `ssh-add ...`, `source ...`).

## Что ты (агент) НЕ ДЕЛАЕШЬ

1. **Никогда не `cat`-ишь и не печатаешь** содержимое:
   - `id_<name>` (приватный SSH-ключ)
   - `<project>/<service>.env` (API-токены, креды)
   - `<project>/<service>.json` (`access_token`, `refresh_token`)
   - `<project>/<service>-cluster.yaml` (kubeconfig bearer tokens)
   - `keys/**` (legacy one-file-per-key dumps)
2. **Никогда не пишешь значение секрета** в:
   - текст чата/ответа
   - commit message, PR description, issue body
   - лог-файлы (`*.log`, debug dumps)
   - скрипты, которые уходят в git
   - env-файлы репозиториев (`.env` в проекте — другое место)
   - URL query string (`?token=...`)
3. **Никогда не копируешь** секрет из `~/.secrets/` в другое место
   без явного указания пользователя.
4. **Никогда не запускаешь** `printenv`, `env | grep TOKEN` и подобное —
   оно может попасть в твой же вывод.
5. **Никогда не используешь устаревший путь `~/.pi/secrets/`** —
   store переехал, все новые секреты идут в `~/.secrets/`.

## Когда грузить skill

Подгружай `/skill:secrets`, если пользователь просит:

- «создай ssh ключ», «сгенерируй ключ для github»
- «сохрани токен», «у меня есть API ключ для X, положи куда надо»
- «что у меня за ключи/токены», «покажи список секретов»
- «дай публичный ключ», «как мне добавить его на github»
- «как мне использовать этот токен в проекте»
- Любая задача, где результат — файл в `~/.secrets/`.

Skill содержит скрипты, которые ты можешь запускать; полная инструкция —
в `~/.pi/agent/skills/secrets/SKILL.md`.

## Стандартные ответы пользователю

Когда ты создал секрет, отвечай в формате:

```
✅ Создано: ~/.secrets/ssh/github/id_github
📋 Публичный ключ (для копирования на github):
   cat ~/.secrets/ssh/github/id_github.pub
🔒 Приватный ключ — посмотри сам:
   cat ~/.secrets/ssh/github/id_github | head -1
🔗 Чтобы добавить в ssh-agent:
   ssh-add ~/.secrets/ssh/github/id_github
```

Никогда не печатай само значение приватного ключа.

## Если пользователь вставляет секрет в чат

Когда пользователь пишет тебе приватный ключ, токен, пароль или
любую credential — **это уже компрометация**: секрет попадает в
transcript сессии, лог терминала, буфер обмена, потенциально в
скриншоты/логи/бэкапы. Правило ниже минимизирует ущерб.

### Что делать

1. **Не повторяй секрет в ответе.** Ни полностью, ни частично
   (`sk-XXXX` тоже нельзя — это часто восстанавливается).
   Вместо этого напиши: «получил, дальше работаю без эха».
2. **Не используй значение в командах как есть.**
   `curl -H "Authorization: Bearer $TOKEN"` — да, через env.
   `curl -H "Authorization: Bearer ghp_abc123..."` — нет, никогда.
3. **Сохрани в `~/.secrets/` и работай оттуда.**
   - SSH ключ → `~/.secrets/ssh/<name>/id_<name>` (через `create-ssh.sh`)
   - API токен → `~/.secrets/<project>/<service>.env`
   - OAuth → `~/.secrets/<project>/<service>.json`
   - Kubeconfig → `~/.secrets/<project>/<service>-cluster.yaml`
   - Account с паролем → **не сохраняй пароль в YAML**. Сохрани только
     login/url, а пароль предложи записать в менеджер паролей.
4. **Подтверди без эха.** После сохранения скажи: «положил в PATH,
   fingerprint first-8-chars: ABCD…EFGH». Никогда полный токен.
5. **Рекомендуй ротацию.** В конце сессии (или сразу, если риск высок):
   «этот токен был в чате — рекомендую rotate. Кнопка: <service settings URL>».

### Распознаваемые паттерны секретов (попадают под правило)

| Тип | Паттерн | Куда сохранять |
|-----|---------|----------------|
| SSH private key | `-----BEGIN OPENSSH PRIVATE KEY-----` … `-----END ...-----` | `ssh/<name>/` |
| PEM private key | `-----BEGIN (RSA\|EC\|DSA\|PGP) PRIVATE KEY-----` | по контексту |
| GitHub PAT | `gh[pousr]_[A-Za-z0-9]{36,255}` | `personal/github.env` |
| GitLab PAT | `glpat-[A-Za-z0-9_-]{20,}` | `<project>/gitlab.env` |
| OpenAI / Anthropic | `sk-[A-Za-z0-9-_]{20,}`, `sk-ant-[A-Za-z0-9-_]{20,}` | `personal/<svc>.env` |
| AWS access key | `AKIA[0-9A-Z]{16}` | `personal/aws.env` |
| Slack | `xox[bpars]-` | `personal/slack.env` |
| Google API | `AIza[0-9A-Za-z_-]{35}` | `personal/gcp.env` |
| HuggingFace | `hf_[A-Za-z0-9]{20,}` | `personal/huggingface.env` |
| npm | `npm_[A-Za-z0-9]{36}` | `personal/npm.env` |
| Прочий сервисный токен | `<SERVICE>_TOKEN=<hex>` | `personal/<service>.env` (плюс `personal/<service>.yaml` с login) |
| Auth в URL | `https?://[^/\s]+:[^@\s]+@` | **вырежи и предупреди**, предложи пересохранить без пароля |
| Authorization header | `Authorization:\s*(Bearer\|Basic\|Token)\s+\S+` | предупреди, не сохраняй |

Если не распознал, но похоже на credential — спроси пользователя:
«это токен? если да — давай сохраним в `~/.secrets/`, а из чата
удали». Не задавай вопросов «это пароль?» в форме которая сама
требует повторения секрета.

### Чего не делать даже в полезных целях

- **Не предлагай** записать секрет в репозиторный `.env`, в
  `~/.bashrc`, в git config `url."https://token@github.com"…`.
  Это другие зоны — пользователь должен решить сам.
- **Не включай секрет в commit message, PR body, issue text,
  documentation, README.** Даже если «для примера».
- **Не делай** `echo $TOKEN | pbcopy` / `Set-Clipboard` и т.п. —
  секрет не должен попасть в clipboard.
- **Не включай** секрет в URL query string (`?token=`).
- **Не используй** устаревший путь `~/.pi/secrets/` — все скрипты
  и skill'ы уже мигрированы на `~/.secrets/`. Также не используй
  type-first пути (`~/.secrets/tokens/`, `accounts/`, `oauth/`) —
  store теперь project-first.

### Если пользователь явно просит секрет обратно в чат

> Не делаю: секрет уже скомпрометирован самим фактом чата.
> Если хочешь проверить что сохранилось правильно — выполни сам:
> `cat ~/.secrets/...` или открой в редакторе.
> Если хочешь ротировать — вот ссылка: <service settings URL>.
