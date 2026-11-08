# Delta Chat Webhook Proxy (C# / .NET 10)

Прокси для вебхуков с поддержкой Delta Chat. Принимает HTTP-запросы и пересылает их
в выбранный чат через JSON-RPC клиент `deltachat-rpc-server`.

## Возможности

- `POST /webhook/{chatTarget}` — универсальный endpoint; принимает `text` / `message` /
  `body`, а также Dokploy-формат `{title, message, timestamp, ...}`.
- `POST /dokploy/{chatTarget}` — выделенный endpoint под Dokploy с тем же форматом
  payload'а.
- `GET /chats` — список всех чатов аккаунта с готовыми webhook-URL.
- `GET /qr` — SecureJoin QR-код для добавления бота в контакты.
- `GET /status` — состояние DC RPC и аккаунта (subprocess, configured, IO).
- `chatTarget` может быть числовым `chat_id` или email-адресом (создаётся контакт + чат
  на лету).
- CORS включён по умолчанию.
- Защита кастомным заголовком: `POST /webhook/*`, `POST /dokploy/*` и `GET /chats`
  (см. «Аутентификация вебхуков»).

### Все эндпоинты

| Метод | Путь | Нужен заголовок | Что возвращает |
|---|---|---|---|
| `POST` | `/webhook/{chatTarget}` | ✅ | `{"status":"sent","chatId":123,"msgId":456}` |
| `GET` | `/webhook/{chatTarget}` | ❌ | описание формата payload'а + поле `required_header` |
| `POST` | `/dokploy/{chatTarget}` | ✅ | то же, что `POST /webhook` |
| `GET` | `/dokploy/{chatTarget}` | ❌ | описание формата payload'а |
| `GET` | `/chats` | ✅ | список чатов: `chat_id`, имя, тип, число участников, готовый `webhook_url` |
| `GET` | `/qr` | ❌ | SVG с SecureJoin QR-кодом |
| `GET` | `/status` | ❌ | снимок состояния DC RPC и аккаунта |
| любой | любой другой путь | ❌ | 400 с подсказкой по доступным путям |

✅ — требуется заголовок из `WEBHOOK_AUTH_HEADER` с правильным значением, иначе 401.
`{chatTarget}` — числовой `chat_id` или email-адрес.

## Структура проекта

```
delta-chat-webhook/
├── src/                       # исходники приложения
│   ├── Program.cs
│   ├── DeltaChatClient.cs
│   ├── Dto/                   # модели запросов/ответов
│   ├── Endpoints/             # HTTP endpoints
│   ├── Infrastructure/        # middleware, bootstrap, фильтры
│   ├── Services/              # форматирование и отправка сообщений
│   └── delta-chat-webhook-client.csproj
├── Dockerfile
├── docker-compose.yml
├── .env                       # локальные настройки (в репозиторий НЕ попадает)
└── .env.example               # шаблон .env (в репозиторий попадает)
```

## Быстрый старт с Dokploy

1. Fork / клонируйте этот репозиторий.
2. В Dokploy создайте Application, подключите репозиторий.
3. Задайте переменные окружения (см. ниже) — либо загрузите в стек файл `.env`.
   Обязательно задайте `WEBHOOK_AUTH_TOKEN` (заголовок для отправителя).
4. Deploy!
5. В настройках уведомлений Dokploy укажите URL:
   `https://your-domain.com/dokploy/alice@example.com`,
   Content-Type `application/json` и заголовок
   `X-Webhook-Token: <тот же секрет, что в WEBHOOK_AUTH_TOKEN>`.

## Переменные окружения

Все настройки лежат в отдельном файле `.env` в корне репозитория (в git он
игнорируется, в репозиторий попадает только шаблон `.env.example`).
Подготовка:

```bash
cp .env.example .env
$EDITOR .env
```

`docker-compose.yml` подхватывает `.env` через `env_file`, так что руками
передавать переменные в `docker compose up` не нужно.

```env
DC_EMAIL=webhook@yourdomain.com
DC_PASSWORD=your_email_password
DC_ACCOUNTS_PATH=/data/accounts                # опционально, по умолчанию /data/accounts
HTTP_PORTS=8082                                  # .NET 8+ стиль (или ASPNETCORE_URLS=http://+:8082)

# SMTP/IMAP — для подмены relay без пересборки (defaults — beget)
SMTP_HOST=smtp.beget.com
SMTP_PORT=465
SMTP_SECURITY=ssl                                # ssl | starttls | plain
IMAP_HOST=imap.beget.com
IMAP_PORT=993
IMAP_SECURITY=ssl

# Аутентификация входящих вебхуков (см. раздел ниже)
WEBHOOK_AUTH_HEADER=X-Webhook-Token              # имя кастомного заголовка
WEBHOOK_AUTH_TOKEN=<случайный секрет>             # пусто = проверка отключена
```

Порт управляется через стандартные .NET env vars: `HTTP_PORTS` (новый стиль, .NET 8+) или
`ASPNETCORE_URLS` (legacy). Не используй `PORT` — фреймворк его не читает, и в Dokploy
это приводит к конфликту с автоматически выставляемым `HTTP_PORTS=8080`.

### Все переменные окружения

| Переменная | Обязательна | По умолчанию | Назначение |
|---|---|---|---|
| `DC_EMAIL` | ✅ | — | email аккаунта бота |
| `DC_PASSWORD` | ✅ | — | пароль от аккаунта бота |
| `DC_ACCOUNTS_PATH` | — | `/data/accounts` | путь к данным аккаунта внутри контейнера |
| `HTTP_PORTS` / `ASPNETCORE_URLS` | — | `8080` (Dokploy) | порт HTTP; `PORT` фреймворком не читается |
| `SMTP_HOST` | — | `smtp.beget.com` | SMTP-релей |
| `SMTP_PORT` | — | `465` | порт SMTP |
| `SMTP_SECURITY` | — | `ssl` | `ssl` \| `starttls` \| `plain` |
| `IMAP_HOST` | — | `imap.beget.com` | IMAP-релей |
| `IMAP_PORT` | — | `993` | порт IMAP |
| `IMAP_SECURITY` | — | `ssl` | `ssl` \| `starttls` \| `plain` |
| `WEBHOOK_AUTH_HEADER` | — | `X-Webhook-Token` | имя кастомного заголовка |
| `WEBHOOK_AUTH_TOKEN` | — | — (проверка отключена) | секрет; пусто → защищённые эндпоинты открыты |

Значения `DC_PASSWORD` и `WEBHOOK_AUTH_TOKEN` живут только в `.env` — в репозиторий
они не попадают (`.env` в `.gitignore`, в git идёт только `.env.example`).

**Смена SMTP/IMAP relay (например если хостер банит IP контейнера):**

```env
# Пример: Mailgun
SMTP_HOST=smtp.mailgun.org
SMTP_PORT=587
SMTP_SECURITY=starttls
IMAP_HOST=imap.mailgun.org
IMAP_PORT=993
IMAP_SECURITY=ssl

# Пример: Yandex (если почта на Яндексе)
SMTP_HOST=smtp.yandex.ru
SMTP_PORT=587
SMTP_SECURITY=starttls
IMAP_HOST=imap.yandex.ru
IMAP_PORT=993
IMAP_SECURITY=ssl
```

После изменения transport нужно **сбросить аккаунт** (иначе DC использует старый transport из кэша):
```bash
docker exec chatbots-delta-chat-y5occb rm -rf /data/accounts/*
# или удалить том deltachat_data в Dokploy
```

## Аутентификация вебхуков

Часть эндпоинтов защищена кастомным заголовком. Имя заголовка и сам секрет
задаются в `.env`:

```env
WEBHOOK_AUTH_HEADER=X-Webhook-Token
WEBHOOK_AUTH_TOKEN=<случайный секрет>
```

Секрет генерируется, например, так:

```bash
openssl rand -hex 32
```

Поведение:

- заголовок с правильным значением → запрос обрабатывается как обычно;
- заголовок отсутствует или значение не совпадает → `401` с JSON
  `{"status":"error","code":401,"message":"..."}`;
- `WEBHOOK_AUTH_TOKEN` пустой → проверка отключена, эндпоинты открыты (в лог при
  старте пишется предупреждение);
- сравнение токена — constant-time, значение заголовка в логи не попадает.

Что защищено:

| Эндпоинт | Почему |
|---|---|
| `POST /webhook/{chatTarget}` | доставляет сообщение в чат от имени бота |
| `POST /dokploy/{chatTarget}` | то же самое для Dokploy |
| `GET /chats` | отдаёт имена чатов, состав групп и `chat_id` — метаданные аккаунта и готовый список целей |

Что осталось открытым: `GET /qr` (нужно, чтобы открыть ссылку на телефоне и
отсканировать), `GET /status` (health-probe для мониторинга) и GET-версии
`/webhook/{chat}` / `/dokploy/{chat}` — они ничего не отправляют и не раскрывают
метаданных аккаунта.

Имя нужного заголовка дублируется в ответе `GET /webhook/{chatTarget}` (поле
`required_header`) и в `Access-Control-Allow-Headers`, чтобы запросы из браузера
проходили preflight.

В Dokploy заголовок добавляется в настройках уведомлений (поле «Headers» /
«Custom Headers»):

```
X-Webhook-Token: <тот же секрет, что в .env>
```

Тот же заголовок нужен любому другому отправителю (CI, Grafana, скрипт):

```bash
curl -X POST https://your-domain.com/webhook/alice@example.com \
  -H "Content-Type: application/json" \
  -H "X-Webhook-Token: <WEBHOOK_AUTH_TOKEN>" \
  -d '{"text":"Hello **Delta Chat**!"}'
```

### Что получит злоумышленник, если эндпоинт оставить открытым

- `POST /webhook/*`, `POST /dokploy/*` — рассылка сообщений от имени бота в любой
  известный `chat_id`. Это имперсонация («сообщение от того, кому доверяют»), поэтому
  закрыты в первую очередь.
- `GET /chats` — имена чатов и групп, их состав, `chat_id` готовых целей. Отправка
  отсюда не следует, но при утечке токена это готовый список целей, поэтому тоже
  закрыт.
- `GET /qr` — SecureJoin-приглашение: кто угодно может его отсканировать своим
  Delta Chat-клиентом и стать контактом бота (получит inbound-почту на `DC_EMAIL`,
  сможет засорять чат). Отправлять от вашего имени он не может. Оставлен открытым,
  чтобы QR можно было открыть на телефоне без заголовков.
- `GET /status` — максимум PID процесса и флаги аккаунта. Оставлен открытым для
  health-checks и мониторинга.

### Если приходит 401

| Сообщение | Причина |
|---|---|
| `Missing required header 'X-Webhook-Token'` | заголовок не передан или передан под другим именем |
| `Invalid value in header 'X-Webhook-Token'` | значение не совпадает с `WEBHOOK_AUTH_TOKEN` |

Типичные причины:

- значение сравнивается посимвольно — лишний пробел или кавычки в `.env` ломают
  совпадение;
- контейнер не перезапущен после правки `.env` (`docker compose up -d --build`);
- отправитель (Dokploy/CI) не добавляет заголовок;
- имя заголовка изменено через `WEBHOOK_AUTH_HEADER` — тогда старое имя больше не
  работает (регистр значения заголовка не важен, имя — да);
- в логах при старте видно, включена ли проверка: `Webhook auth enabled: header '...'`
  либо предупреждение `WEBHOOK_AUTH_TOKEN is not set — ...`.

## Локальный запуск

```bash
HTTP_PORTS=8082 dotnet run --project src/delta-chat-webhook-client.csproj
```

Требуется установленный `deltachat-rpc-server` (Docker-образ уже включает его).

## Docker build/run

```bash
# docker compose — переменные подхватываются из .env
docker compose up -d --build

# либо вручную, без .env
docker build -t delta-chat-webhook .
docker run --rm -p 8082:8082 \
  -e HTTP_PORTS=8082 \
  -e DC_EMAIL=webhook@yourdomain.com \
  -e DC_PASSWORD=your_email_password \
  delta-chat-webhook
```

## Использование

Общие значения для примеров:

```bash
TOKEN="<WEBHOOK_AUTH_TOKEN>"    # значение из .env
BASE="https://your-domain.com"
```

### Все команды

```bash
# 1. Отправить сообщение в чат (универсальный webhook)
curl -X POST "$BASE/webhook/alice@example.com" \
  -H "Content-Type: application/json" \
  -H "X-Webhook-Token: $TOKEN" \
  -d '{"text": "Hello **Delta Chat**!"}'

# 2. Dokploy-формат: title + message (+ timestamp)
curl -X POST "$BASE/dokploy/alice@example.com" \
  -H "Content-Type: application/json" \
  -H "X-Webhook-Token: $TOKEN" \
  -d '{
    "title": "Test Notification",
    "message": "Hi, From Dokploy 👋",
    "timestamp": "2025-12-07T19:41:53.470Z"
  }'

# 3. Список чатов и chat_id (заголовок обязателен)
curl "$BASE/chats" -H "X-Webhook-Token: $TOKEN"

# 4. QR-код для добавления бота в контакты (открытый эндпоинт)
curl "$BASE/qr" -o qr.svg

# 5. Диагностика (открытый эндпоинт)
curl "$BASE/status"
```

### Формат payload'а

`POST /webhook/{chatTarget}` — если в payload'е есть `title` или `message`,
используется Dokploy-формат; иначе берётся первое непустое из `text`, `message`,
`body`:

- `title` → `**жирный**` первой строкой;
- прочие ключи → `**key**: value`;
- `timestamp` (ISO-8601) → подпись `*Время: 2025-12-07 19:41:53*`.

`POST /dokploy/{chatTarget}` — только Dokploy-формат: обязательно `title` или
`message`, иначе 400.

Пустое сообщение, не-JSON или JSON не-объект → 400 с описанием поддерживаемых
форматов.

Ответ обоих POST'ов: `{"status":"sent","chatId":123,"msgId":456}`. `msgId` означает,
что сообщение принято и поставлено в очередь Delta Chat, а не что оно доставлено
получателю (доставка идёт через SMTP-релей и может отвалиться позже).

### Узнать chat_id

```bash
curl https://your-domain.com/chats \
  -H "X-Webhook-Token: <WEBHOOK_AUTH_TOKEN>"
```

Ответ содержит `chat_id` (его можно подставить в `/webhook/{chat_id}`) и готовый
`webhook_url` для каждого чата.

### Диагностика

```bash
curl https://your-domain.com/status
```

Возвращает JSON со снимком состояния `deltachat-rpc-server` и аккаунта:
`rpc_subprocess_alive`, `rpc_subprocess_pid`, `account_id`, `account_configured`,
`io_running`, `error`, `elapsed_ms`.
Полезно когда сообщения перестали доходить — сразу видно, умер ли субпроцесс,
отвалилась ли учётка или просто SMTP заблокировал.
