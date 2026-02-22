# ILocks.Qr

Backend-сервис для входа по телефону, генерации QR-кодов доступа, хранения истории и отправки QR в Telegram.

## Функциональность

1. Вход по номеру телефона.
2. Генерация QR-кода по данным: `checkInAt`, `checkOutAt`, `guestsCount`, `doorPassword`.
3. Хранение QR и метаданных в PostgreSQL.
4. Просмотр истории QR текущего пользователя.
5. Просмотр конкретного QR по `id`.
6. Отправка сохраненного QR в Telegram-чат пользователя.

## Технологии

- `C#`, `.NET 8`, `ASP.NET Core Minimal API`
- `PostgreSQL 16`
- `EF Core 8`, `Npgsql`
- `JWT Bearer`
- `FluentValidation`
- `QRCoder`
- `Telegram.Bot`
- `Swagger / OpenAPI`
- `xUnit`, `FluentAssertions`, `coverlet.collector`

## Структура решения

- `Api` - HTTP API, endpoint-ы, DI, конфигурация, валидация.
- `Application` - контракты use-case и сервисов.
- `Domain` - сущности предметной области.
- `Infrastructure` - workflow-логика, EF Core, интеграции (QR/Telegram/JWT/OTP).
- `UnitTests` - unit-тесты.

## Требования

- `.NET SDK 8.0+`
- `PostgreSQL 16+`
- `Docker` и `Docker Compose` (опционально)

## Конфигурация

### Обязательные настройки

`Api/Configuration/ServiceCollectionExtensions.cs` валидирует конфиг при старте.

- `ConnectionStrings:Default` - строка подключения к PostgreSQL.
- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:Key` - минимум 32 символа.
- `Telegram:BotToken` - ключ должен существовать в конфиге (может быть пустым для локальной разработки без Telegram).

### Файлы конфигурации

- `Api/appsettings.json` - шаблон.
- `Api/appsettings.Development.json` - локальные значения по умолчанию.
- `docker/.env.example` - шаблон переменных для Docker.

### Переменные Docker (`docker/.env`)

| Переменная | Назначение | Пример |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Окружение ASP.NET Core | `Development` |
| `API_PORT` | Порт API на хосте | `8080` |
| `POSTGRES_PORT` | Порт PostgreSQL на хосте | `5432` |
| `POSTGRES_DB` | Имя БД | `ilocks_qr` |
| `POSTGRES_USER` | Пользователь БД | `ilocks` |
| `POSTGRES_PASSWORD` | Пароль БД | `ilocks_dev_password` |
| `JWT_ISSUER` | Issuer JWT | `ILocks.Qr` |
| `JWT_AUDIENCE` | Audience JWT | `ILocks.Qr.Client` |
| `JWT_KEY` | Ключ подписи JWT (>=32) | `CHANGE_ME_TO_A_LONG_RANDOM_KEY_32_PLUS` |
| `TELEGRAM_BOT_TOKEN` | Токен Telegram-бота | `<secret>` |

## Быстрый старт (локально)

1. Подготовить env-файл для Docker:

```powershell
Copy-Item docker/.env.example docker/.env
```

2. Поднять PostgreSQL (через docker):

```powershell
docker compose --env-file docker/.env -f docker/docker-compose.yml up -d db
```

3. Применить миграции:

```powershell
dotnet tool update --global dotnet-ef
dotnet ef database update --project Infrastructure/Infrastructure.csproj --startup-project Api/Api.csproj
```

4. Запустить API:

```powershell
dotnet run --project Api/Api.csproj
```

5. Открыть Swagger:

- `http://localhost:5214/swagger` (профиль `http`)
- `https://localhost:7194/swagger` (профиль `https`)

## Быстрый старт (полностью в Docker)

1. Создать env-файл:

```powershell
Copy-Item docker/.env.example docker/.env
```

2. Запустить сервисы:

```powershell
docker compose --env-file docker/.env -f docker/docker-compose.yml up --build -d
```

3. Swagger:

- `http://localhost:8080/swagger`

Примечание: миграции БД нужно применить отдельно (`dotnet ef database update ...`) до начала полноценной работы с API.

## Основной сценарий работы API

1. `POST /api/auth/request-otp` - запросить OTP по номеру телефона.
2. `POST /api/auth/confirm-otp` - подтвердить OTP, получить JWT.
3. `POST /api/telegram/bind-chat` - привязать `chatId` Telegram (опционально, нужен для отправки QR).
4. `POST /api/qr` - создать QR.
5. `GET /api/qr` - получить историю QR.
6. `GET /api/qr/{id}` - получить детали QR.
7. `POST /api/qr/{id}/send-telegram` - отправить QR в Telegram.

## API (кратко)

| Method | Route | Auth | Назначение |
|---|---|---|---|
| GET | `/health` | Нет | Проверка работоспособности |
| POST | `/api/auth/request-otp` | Нет | Запрос OTP |
| POST | `/api/auth/confirm-otp` | Нет | Подтверждение OTP и получение JWT |
| POST | `/api/telegram/bind-chat` | Bearer | Привязка Telegram-чата |
| POST | `/api/qr` | Bearer | Генерация и сохранение QR |
| GET | `/api/qr` | Bearer | История QR |
| GET | `/api/qr/{id}` | Bearer | Детали конкретного QR |
| POST | `/api/qr/{id}/send-telegram` | Bearer | Отправка QR в Telegram |

## Пример запросов

### 1) Request OTP

`POST /api/auth/request-otp`

```json
{
  "phoneNumber": "+7 (999) 123-45-67"
}
```

В `Development` ответ может содержать `debugCode`.

### 2) Confirm OTP

`POST /api/auth/confirm-otp`

```json
{
  "phoneNumber": "79991234567",
  "code": "123456"
}
```

### 3) Create QR

`POST /api/qr` (Bearer token обязателен)

```json
{
  "checkInAt": "2026-02-23T14:00:00Z",
  "checkOutAt": "2026-02-27T11:00:00Z",
  "guestsCount": 2,
  "doorPassword": "LOCK-9371",
  "dataType": "booking_access"
}
```

### 4) Bind Telegram chat

`POST /api/telegram/bind-chat` (Bearer token обязателен)

```json
{
  "chatId": 123456789
}
```

## Статусы и ошибки (основные)

| Группа | Код | Причина |
|---|---|---|
| OTP | `400` | Невалидный телефон/код, OTP отсутствует или истек |
| OTP | `429` | Превышен лимит попыток OTP |
| QR | `400` | Ошибки валидации запроса |
| QR | `401` | Отсутствует или некорректный Bearer token |
| QR | `404` | QR не найден |
| Telegram | `400` | Невалидный chatId или чат не привязан |
| Telegram | `403` | Бот не имеет доступа к чату |
| Telegram | `502/503/504` | Ошибка Telegram API, сети или таймаут |

## Валидация `CreateQrRequest`

- `CheckInAt < CheckOutAt`
- `CheckOutAt > now (UTC)`
- `GuestsCount` в диапазоне `1..50`
- `DoorPassword` обязателен, максимум `128`
- `DataType` обязателен, максимум `64`

## Тесты

Запуск unit-тестов:

```powershell
dotnet test UnitTests/UnitTests.csproj
```

С покрытием:

```powershell
dotnet test UnitTests/UnitTests.csproj --collect:"XPlat Code Coverage"
```

## Полезные команды

Сборка:

```powershell
dotnet build ILocks.Qr.sln
```

Проверка миграций:

```powershell
dotnet ef migrations list --project Infrastructure/Infrastructure.csproj --startup-project Api/Api.csproj
```

## Соответствие ТЗ

- Вход по номеру телефона: реализовано (`/api/auth/*`).
- Генерация QR и хранение метаданных: реализовано (`/api/qr` + PostgreSQL).
- Просмотр списка/истории QR: реализовано (`GET /api/qr`).
- Просмотр конкретного QR: реализовано (`GET /api/qr/{id}`).
- Отправка QR в Telegram: реализовано (`POST /api/qr/{id}/send-telegram`).
- Swagger-спецификация API: реализовано (`/swagger` в Development).
- Руководство программиста: этот `README.md`.
