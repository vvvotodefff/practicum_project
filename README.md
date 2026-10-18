# ProjectWork — спринт 9

Учебный REST API на C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL.
Выполнен этап 2: монолит разделён на три независимых сервиса.
Репозиторий: [practicum_project](https://github.com/vvvotodefff/practicum_project).

## Сервисы

| Сервис | Ответственность | Swagger | Своя PostgreSQL |
| --- | --- | --- | --- |
| Users | Регистрация, вход, SHA-256, выдача JWT | http://localhost:5250/swagger | localhost:5441 / users_db |
| Events | CRUD событий, свободные места | http://localhost:5251/swagger | localhost:5442 / events_db |
| Bookings | Создание, чтение, отмена и фоновое подтверждение броней | http://localhost:5252/swagger | localhost:5443 / bookings_db |

Каждая база имеет отдельного пользователя и том Docker. Сервисы не обращаются по HTTP
друг к другу и не читают чужие таблицы. В Bookings хранятся только внешние
идентификаторы EventId и UserId; навигаций и внешних ключей на другие сервисы нет.

## Структура

```text
Services/
  Users/
    ProjectWork.Users.Domain/
    ProjectWork.Users.Application/
    ProjectWork.Users.Infrastructure/
    ProjectWork.Users.Presentation/
  Events/
    ProjectWork.Events.Domain/
    ProjectWork.Events.Application/
    ProjectWork.Events.Infrastructure/
    ProjectWork.Events.Presentation/
  Bookings/
    ProjectWork.Bookings.Domain/
    ProjectWork.Bookings.Application/
    ProjectWork.Bookings.Infrastructure/
    ProjectWork.Bookings.Presentation/
Shared/
  ProjectWork.Contracts/
ProjectWork.Tests/
ProjectWork.IntegrationTests/
ProjectWork.PresentationTests/
ProjectWork.slnx
docker-compose.yml
```

Domain не зависит от других слоёв; Application зависит от своего Domain.
Infrastructure реализует порты Application. Presentation подключает свои Application
и Infrastructure. Между сервисами нет ProjectReference.
Application возвращает DTO; сохранение выполняет локальный IUnitOfWork.
Фоновый обработчик Bookings создаёт scope через IServiceScopeFactory.

Shared/ProjectWork.Contracts выделен под публичные сообщения. Сам контракт
BookingConfirmed и имя топика относятся к этапу 3 и пока не добавлены.

## Запуск

Нужны .NET SDK 10 и Docker Desktop с Linux-контейнерами.
В корне репозитория:

```powershell
docker compose up -d --wait
dotnet restore ProjectWork.slnx
dotnet build ProjectWork.slnx --configuration Release
```

В трёх отдельных терминалах:

```powershell
dotnet run --project Services/Users/ProjectWork.Users.Presentation --launch-profile http
```

```powershell
dotnet run --project Services/Events/ProjectWork.Events.Presentation --launch-profile http
```

```powershell
dotnet run --project Services/Bookings/ProjectWork.Bookings.Presentation --launch-profile http
```

При старте каждый сервис применяет только свои миграции. В Visual Studio откройте
ProjectWork.slnx и назначьте три проекта Presentation запускаемыми проектами.

На этапе 2 compose запускает только три БД. Kafka и запуск API в контейнерах —
следующие этапы. Остановка БД без удаления данных: `docker compose down`.

### Настройки

Строки подключения находятся в appsettings.json соответствующих Presentation.
Учебные логины/пароли БД: users / users-development, events / events-development,
bookings / bookings-development. Порты PostgreSQL привязаны к localhost.

JWT выдаёт только Users; Events и Bookings проверяют его подпись, срок, issuer,
audience и claims. Во всех трёх сервисах Secret, Issuer и Audience должны совпадать.
Существующая защита из спринта 8 сохранена при переносе.

Учебный JWT-ключ есть только в appsettings.Development.json. В базовом appsettings.json
Secret пуст: Production без явно заданного ключа не запустится.
Для переопределения используйте переменные окружения:

```powershell
$env:Jwt__Secret = "<общий случайный секрет: минимум 32 байта UTF-8>"
$env:Jwt__Issuer = "ProjectWork.Api"
$env:Jwt__Audience = "ProjectWork.Client"
$env:Jwt__LifetimeMinutes = "60"
# Для каждого процесса — строка подключения к его собственной БД:
$env:ConnectionStrings__DefaultConnection = "<строка подключения данного сервиса>"
```

## API

| Сервис | Метод и маршрут | Доступ / результат |
| --- | --- | --- |
| Users | POST /auth/register | Публично, 204; ошибка 400 |
| Users | POST /auth/login | Публично, 200 с полем token; ошибка 400 |
| Events | GET /events | Публично, фильтры title/from/to, пагинация page/pageSize |
| Events | GET /events/{id} | Публично, 200 / 404 |
| Events | POST /events | Admin, 201 с Location |
| Events | PUT /events/{id} | Admin, 204 / 404 |
| Events | DELETE /events/{id} | Admin, 204 / 404 |
| Bookings | POST /events/{id}/book | Авторизованный пользователь, 202 с Location |
| Bookings | GET /bookings/{id} | Владелец или Admin; чужая и отсутствующая бронь дают 404 |
| Bookings | DELETE /bookings/{id} | Владелец или Admin, 204; чужая — 403 |

Создание брони отправляется **на порт Bookings (5252)**, хотя путь начинается с /events.
UserId берётся из проверенного JWT. Лимит — 10 активных броней пользователя
(Pending и Confirmed). Повторная отмена безопасна.
Фоновый сервис переводит Pending в Confirmed и сохраняет ProcessedAt.

Для ручной проверки зарегистрируйтесь в Users, получите token через /auth/login
и вставьте его в Authorize в Swagger нужного сервиса (без префикса Bearer).
Для создания события нужна роль Admin. Затем скопируйте id события в запрос
создания брони на Bookings и получите бронь по Location.

Title обязателен (до 200 символов), Description — до 2000. StartAt и EndAt обязательны,
EndAt > StartAt, TotalSeats > 0. Events сохраняет занятые места при изменении вместимости.
Время хранится в UTC. Ошибки — Problem Details; неверная модель — 400,
отсутствующий/невалидный JWT — 401, недостаточные права — 403.
Swagger показывает замок только на защищённых операциях.

## Граница выполненного этапа

На этапе 2 подтверждение и отмена брони меняют только bookings_db.
Количество мест в events_db от этих действий пока не меняется: публикация
BookingConfirmed и обработка в Events будут реализованы на этапах 3–5.

Bookings не может синхронно проверить существование события, дату его начала или
оставшиеся места через чужую базу. Сейчас принимается любой непустой EventId.
Проверка этих фактов требует обмена сообщениями.
Один будущий BookingConfirmed сам по себе не обеспечивает синхронный отказ при
переполнении, возврат места при отмене или проверку прошедшего события; для таких
гарантий потребуется расширение протокола.

Удаление события больше не каскадно удаляет брони из другой базы.
Старые данные монолитной eventapi не переносятся автоматически: новые БД создаются
пустыми. Старый том eventapi_pgdata не удалялся; прежняя схема доступна в истории Git.

Учебные ограничения спринта 8 сохранены: регистрация позволяет выбрать Admin,
используется SHA-256 без соли. Для реального сервиса нужны отдельное назначение ролей
и парольный KDF. Семафоры защищают изменения внутри одного процесса, не нескольких реплик.

## Миграции

У каждого Infrastructure есть собственные InitialCreate, designer и snapshot:

- UsersDbContext: только users.
- EventsDbContext: только events.
- BookingsDbContext: только bookings, без FK в чужие базы.

Пример следующей миграции (для других сервисов замените Users):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet ef migrations add MigrationName --project Services/Users/ProjectWork.Users.Infrastructure --startup-project Services/Users/ProjectWork.Users.Presentation --context UsersDbContext --output-dir Persistence/Migrations
```

Ручное применение существующей миграции:

```powershell
dotnet ef database update --project Services/Users/ProjectWork.Users.Infrastructure --startup-project Services/Users/ProjectWork.Users.Presentation --context UsersDbContext
```

Миграции старого общего AppDbContext в новые базы применять нельзя.

## Проверки

```powershell
dotnet test ProjectWork.slnx --configuration Release --verbosity minimal
```

- ProjectWork.Tests: доменные правила, DTO, сервисы, хеширование/JWT,
  зависимости слоёв и модели трёх InMemory-контекстов.
- ProjectWork.IntegrationTests: репозитории и отдельные миграции на PostgreSQL,
  независимые базы и отсутствие межсервисных FK.
- ProjectWork.PresentationTests: три HTTP-конвейера, JWT, 401/403/404,
  маршруты, CRUD, отмена, фоновая обработка и Swagger.

Тесты сами создают и удаляют временные контейнеры и базы. Рабочие БД compose не используются.
CI в .github/workflows/ci.yml собирает решение и запускает тесты при push в main/sprint-*
и PR в main. Для отдельного прогона HTTP-проверок:
`./scripts/Verify-Api.ps1 -Configuration Release`.

Чек-лист спринта 8 — исторический отчёт для прежнего монолита, не текущей структуры.
