# Сервис управления мероприятиями

Учебный ASP.NET Core Web API. В восьмом спринте добавлены пользователи,
JWT-аутентификация, роли и отмена бронирований. Проект разделён на четыре
производственные сборки: Domain, Application, Infrastructure и Presentation.
Тестовые проекты ссылаются на проверяемые слои, HTTP-тесты выделены отдельно.
События и бронирования хранятся в PostgreSQL через Entity Framework Core.
Сервисы работают через репозитории, а схема БД управляется миграциями EF Core.
После перезапуска API данные сохраняются.

[Репозиторий на GitHub](https://github.com/vvvotodefff/practicum_project).
Результаты проверки восьмого спринта: [чек-лист](SPRINT-8-CHECKLIST.md).

`ProjectWork.Domain` — отдельная библиотека с `Event`, `Booking`, `User`, `BookingStatus`, `UserRole`
и доменными исключениями. Она не зависит от других проектов, EF Core, ASP.NET Core,
JSON-сериализации или DataAnnotations. Правила создания и обновления проверяют
сами сущности, ошибки передаются через `DomainValidationException` и преобразуются
в HTTP 400 в middleware. Контроллеры возвращают `EventInfo` и `BookingInfo` без
навигационных свойств, поэтому формат JSON не зависит от графа сущностей.

`ProjectWork.Application` содержит сервисы, их интерфейсы, интерфейсы репозиториев,
DTO, порт `IUnitOfWork`, `PaginatedResult<T>` и общий семафор записи `EventWriteLock`.
Из проектов библиотека зависит только от Domain, без ссылок на Infrastructure,
EF Core и ASP.NET Core. Для `AddApplicationServices()` подключён только пакет
`Microsoft.Extensions.DependencyInjection.Abstractions`.
XML-комментарии DTO подключены к Swagger из сборки Application.

`ProjectWork.Infrastructure` содержит `AppDbContext`, Fluent API-конфигурации,
существующие миграции, реализации репозиториев, `UnitOfWork` и `BookingProcessingService`.
Зависит от Application и Domain, но не от веб-проекта. EF Core, Npgsql и абстракции
фонового выполнения подключены здесь; фоновый сервис через `IServiceScopeFactory`
обращается к интерфейсам Application.

`AddInfrastructureServices(configuration)` регистрирует контекст, репозитории
и `IUnitOfWork` как Scoped, а фоновый сервис — как Hosted Service.
Метод вызывается из `Program.cs`.
Строка подключения остаётся в конфигурации веб-проекта, её отсутствие проверяется
при регистрации. Идентификатор владельца брони API получает из проверенного JWT.

Веб-проект `ProjectWork.Presentation` выполняет роль Presentation: контроллеры принимают HTTP-запросы,
вызывают интерфейсы Application и возвращают полученные DTO с нужными HTTP-статусами.
Сервисы Application возвращают `EventInfo`, `BookingInfo` и их коллекции;
маппинг доменных сущностей выполняется внутри Application.
Правила дат, мест и статусов бронирований остаются в Application и Domain.
`ExceptionHandlingMiddleware` преобразует исключения в Problem Details.

`Program.cs` — точка сборки приложения: вызывает `AddApplicationServices()`,
`AddInfrastructureServices(configuration)` и `AddPresentationServices(configuration)`,
инициализирует базу через `MigrateDatabaseAsync()` и настраивает HTTP-конвейер.
Регистрация контроллеров, JSON и Swagger находится в `DependencyInjection.cs`
веб-проекта. В `Program.cs` нет прямой работы с EF Core или `AppDbContext`.

Зависимости проектов заданы через `ProjectReference`:

| Проект | Назначение | Ссылки на проекты |
|---|---|---|
| `ProjectWork.Domain` | Сущности, доменные правила и исключения | Нет |
| `ProjectWork.Application` | Сервисы, DTO, порты репозиториев | Domain |
| `ProjectWork.Infrastructure` | EF Core, PostgreSQL, репозитории, фоновая обработка | Application, Domain |
| `ProjectWork.Presentation` | Presentation: HTTP, middleware, composition root | Application, Infrastructure |
| `ProjectWork.Tests` | Юнит- и архитектурные тесты | Domain, Application, Infrastructure |
| `ProjectWork.IntegrationTests` | Репозитории и миграции в PostgreSQL | Domain, Application, Infrastructure |
| `ProjectWork.PresentationTests` | Контроллеры и HTTP-маппинг ошибок | Presentation и используемые при проверке слои |

Только HTTP-тестам нужна ссылка на веб-проект. Общие и интеграционные тесты
больше не зависят от Presentation, в том числе в скомпилированных runtime-зависимостях.

## Безопасность и JWT

Application содержит порты `IPasswordHasher` и `IJwtTokenGenerator` в
`Abstractions/Security`. Их реализации находятся в `Infrastructure/Security`
и подключаются методом `AddSecurityServices(configuration)` при регистрации Infrastructure.

`Sha256PasswordHasher.Hash` вычисляет SHA-256 пароля в UTF-8 и возвращает HEX-строку.
`Verify` проверяет пароль сравнением хешей за постоянное время; некорректный формат
сохранённого хеша возвращает `false`. Пароль не обрезается и не переводится в другой регистр.
Простой SHA-256 выбран согласно учебному заданию; для реальной системы нужен
специализированный алгоритм хранения паролей с солью и настраиваемой стоимостью.

`JwtTokenGenerator.Generate(user)` выдаёт подписанный HS256-токен с claims:
`sub` (Id пользователя), `name` (логин), `role` (`User` или `Admin`),
`iss`, `aud`, `iat`, `nbf`, `exp` и уникальным `jti`. Хеш пароля в токен не попадает.
Время берётся из `TimeProvider`, поэтому срок действия проверяется детерминированными тестами.

В `ProjectWork.Presentation/appsettings.json` добавлена секция `Jwt`:
`Secret`, `Issuer`, `Audience`, `LifetimeMinutes`. Настройки проверяются при регистрации:
ключ должен содержать минимум 32 байта UTF-8, издатель и аудитория обязательны,
срок действия задаётся положительным целым числом минут.
В основном `appsettings.json` поле `Jwt:Secret` пустое. Учебный ключ находится только
в `appsettings.Development.json` и загружается профилями http/https с окружением Development.
В Production и Staging приложение без явно заданного ключа не запускается.
Задайте `Jwt__Secret` через секрет-хранилище или окружение; настоящий ключ не коммитьте.

Регистрация и вход реализованы в `UserService`, проверка токена — штатным JwtBearer
в Presentation. Проверяются подпись HS256, издатель, аудитория, срок действия,
идентификатор пользователя и роль. Claims `name` и `role` используются для имени и роли.
В конвейере `UseAuthentication()` вызывается перед `UseAuthorization()`.
Фильтр операций Swagger добавляет Bearer-защиту только для Authorize;
у публичных операций и AllowAnonymous замков нет.
При неверном логине и неверном пароле возвращается одинаковое сообщение с HTTP 400.
Логины обрезаются по краям; регистр учитывается при поиске и проверке уникальности.

Для рабочего окружения задайте длинный случайный секрет через секрет-хранилище
или переменную `Jwt__Secret` (например, 32 случайных байта, закодированных в Base64).
Не используйте опубликованный учебный ключ. Издатель и аудитория
переопределяются через `Jwt__Issuer` и `Jwt__Audience`, срок — через `Jwt__LifetimeMinutes`.

## Требования и запуск

- .NET SDK 10.
- Docker Desktop с Linux-контейнерами для PostgreSQL и интеграционных тестов.
  Сам API также можно подключить к отдельно установленной PostgreSQL 16.
- PowerShell 7 для необязательного сценария проверки `scripts/Verify-Api.ps1`.

Из корня репозитория:

```bash
docker compose up -d --wait postgres
dotnet restore
dotnet build
dotnet run --project ProjectWork.Presentation/ProjectWork.Presentation.csproj --launch-profile http
```

API: [http://localhost:5259/events](http://localhost:5259/events).
Swagger UI: [http://localhost:5259/swagger](http://localhost:5259/swagger).
Для HTTPS используйте профиль `https`: адрес `https://localhost:7167`.
При необходимости доверьте локальный сертификат командой `dotnet dev-certs https --trust`.

## PostgreSQL и миграции

Используется compose-файл из задания: образ `postgres:16-alpine`,
контейнер `eventapi-postgres`, база `eventapi`.
Порт хоста изменён на `127.0.0.1:5433`, так как `5432` занят локальной службой.
Внутри контейнера сервер слушает стандартный порт `5432`.

В `appsettings.json` настроена строка:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=eventapi;Username=postgres;Password=postgres"
  }
}
```

Это учётные данные для локального обучения. Для другого окружения задайте
`ConnectionStrings__DefaultConnection` через переменную окружения. Например, в PowerShell:

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5433;Database=eventapi;Username=postgres;Password=postgres"
dotnet run --project ProjectWork.Presentation/ProjectWork.Presentation.csproj --launch-profile http
```

При запуске API вызывает `app.Services.MigrateDatabaseAsync()`. Этот метод из
Infrastructure создаёт scope и вызывает `Database.MigrateAsync()` до запуска
HTTP-конвейера и фонового сервиса.
Начальная миграция `20261006154025_InitialCreate` уже включена в репозиторий:
на пустой БД она создаёт `events`, `bookings`, ключи, индексы и ограничения.
Миграция `20261016070539_AddUsersAndBookingOwnership` добавляет таблицу `users`,
уникальный индекс логина и внешний ключ `bookings.user_id → users.id`.
Если в базе есть брони предыдущего спринта, они сохраняются и получают технического
владельца `__legacy_bookings_000000000801` без известного пароля.
Администратор может отменять такие брони. На пустой базе технический пользователь не создаётся.
Применённые миграции записываются в `__EFMigrationsHistory`.
Повторный запуск применяет только новые миграции, существующие данные сохраняются.
Сервер PostgreSQL должен быть доступен до запуска API; ручной SQL для создания схемы не нужен.

Для CLI-команд нужен `dotnet-ef` той же версии, что пакеты EF Core проекта:

```bash
dotnet tool install --global dotnet-ef --version 10.0.12
```

Если инструмент уже установлен, вместо `install` выполните
`dotnet tool update --global dotnet-ef --version 10.0.12`.

Из корня репозитория:

```bash
# Посмотреть миграции и применить их без запуска API
dotnet ef migrations list --project ProjectWork.Infrastructure/ProjectWork.Infrastructure.csproj --startup-project ProjectWork.Presentation/ProjectWork.Presentation.csproj
dotnet ef database update --project ProjectWork.Infrastructure/ProjectWork.Infrastructure.csproj --startup-project ProjectWork.Presentation/ProjectWork.Presentation.csproj

# Проверить, не разошлись ли модель и снимок миграции
dotnet ef migrations has-pending-model-changes --project ProjectWork.Infrastructure/ProjectWork.Infrastructure.csproj --startup-project ProjectWork.Presentation/ProjectWork.Presentation.csproj

# Только после изменения модели: создать НОВУЮ миграцию с осмысленным именем
dotnet ef migrations add DescribeModelChange --project ProjectWork.Infrastructure/ProjectWork.Infrastructure.csproj --startup-project ProjectWork.Presentation/ProjectWork.Presentation.csproj --output-dir Persistence/Migrations
```

Последняя команда — пример для дальнейшей разработки, для обычного запуска она не нужна.
Не создавайте `InitialCreate` повторно. Миграции, их Designer-файлы и
`AppDbContextModelSnapshot` хранятся в Git вместе с кодом.

`--project` указывает на сборку Infrastructure с контекстом и миграциями,
`--startup-project` — на веб-проект с настройками запуска и строкой подключения.
Путь `--output-dir` считается от Infrastructure. Пакет `Microsoft.EntityFrameworkCore.Design`
оставлен в startup-проекте для работы `dotnet ef`; runtime-пакеты EF Core и Npgsql
перенесены в Infrastructure. Начальная миграция перенесена вместе с Designer и снимком
модели без изменения идентификатора и операций: существующую базу пересоздавать не нужно.

`EnsureCreated` больше не используется и не должен смешиваться с миграциями.
Если у вас осталась старая БД, созданная через `EnsureCreated`, перед переходом
сохраните нужные данные и укажите новую пустую учебную БД в строке подключения.
Старая схема без истории миграций автоматически не преобразуется.

Проверка и остановка контейнера:

```bash
docker compose ps
docker compose exec postgres psql -U postgres -d eventapi -c "\dt"
docker compose stop postgres
```

Данные находятся в именованном томе `eventapi_pgdata`
(по умолчанию `projectwork_eventapi_pgdata`). Остановка или обычный
`docker compose down` не удаляют том; `down -v` удаляет и данные.

## API

| Метод | Маршрут | Результат |
|---|---|---|
| GET | /events | 200 — страница событий |
| GET | /events/{id} | 200 — событие; 404 — не найдено |
| POST | /events | Admin; 201 — событие и Location |
| PUT | /events/{id} | Admin; 204 — обновление; 404 — не найдено |
| DELETE | /events/{id} | Admin; 204 — удаление с бронями; 404 — не найдено |
| POST | /events/{id}/book | User/Admin; 202 — бронь; 400 — событие началось; 404 — нет события; 409 — нет мест или превышен лимит |
| GET | /bookings/{id} | Владелец/Admin; 200 — бронь; 404 — отсутствует или принадлежит другому пользователю |
| DELETE | /bookings/{id} | Владелец/Admin; 204 — отмена; 403 — чужая бронь; 404 — не найдена |
| POST | /auth/register | Без токена; 204 — регистрация; 400 — валидация или занятый логин |
| POST | /auth/login | Без токена; 200 — объект с полем token; 400 — неверные учётные данные |

Свойства `Booking` доступны только для чтения снаружи: статус меняется через
`Confirm`, `Reject` и `Cancel`, а владелец назначается при создании.

События возвращают `id`, `title`, `description`, `startAt`, `endAt`,
`totalSeats`, `availableSeats`.
Клиент не задаёт идентификаторы и количество свободных мест: для создания
и обновления используются DTO `CreateEvent` и `UpdateEvent`.

Параметры `GET /events`:

| Параметр | Назначение |
|---|---|
| title | Подстрока названия без учёта регистра |
| from | Начало события не раньше указанной даты |
| to | Окончание события не позже указанной даты |
| page | Номер страницы от 1, по умолчанию 1 |
| pageSize | Размер страницы от 1, по умолчанию 10 |

Фильтры применяются вместе, сортировка — по `StartAt`, затем `Id`.
Ответ содержит `totalCount`, `page`, `pageSize`, `items`.
Фильтрация, подсчёт и пагинация выполняются в БД.

## Роли и бизнес-правила

- GET /events и GET /events/{id} доступны без токена.
- Создание, изменение и удаление событий требуют роли Admin.
- Бронирование, чтение и отмена брони требуют действующего токена.
- User может читать и отменять только свои брони, Admin — любые.
- Чтение чужой брони возвращает тот же 404, что и чтение отсутствующей; отмена чужой — 403.
- UserId не передаётся клиентом в теле бронирования: API берёт его из claim sub.
- Событие нельзя бронировать, если StartAt <= текущее время UTC.
- Активные брони — Pending и Confirmed. Лимит — 10 на пользователя по всем событиям.
- Cancelled и Rejected не занимают лимит. При отмене место возвращается ровно один раз.
- Отмена переводит бронь в Cancelled, а не удаляет её из базы.
- Без токена защищённые операции возвращают 401, при недостатке прав — 403.

Возможность выбрать Admin при публичной регистрации оставлена **по условию учебного задания**.
В реальном публичном API назначение роли администратора должно выполняться отдельно.

## Сценарий проверки через Swagger

1. Запустите базу и API, откройте `/swagger`.
2. Выполните `POST /auth/register` без токена:

   ```json
   { "login": "student", "password": "training-password", "role": "User" }
   ```

   Успех — 204. Поле role можно опустить: будет User.
3. Выполните `POST /auth/login` с тем же логином и паролем:

   ```json
   { "login": "student", "password": "training-password" }
   ```

   Из ответа `{ "token": "..." }` скопируйте только строку токена.
4. Попробуйте `POST /events` до авторизации: ожидается 401.
5. Нажмите **Authorize** вверху Swagger, вставьте токен **без слова Bearer**,
   нажмите Authorize и Close. Повторите создание события: ожидается 403 для User.
6. Зарегистрируйте второго пользователя с `"role": "Admin"`, войдите через login.
   В Authorize нажмите Logout, затем вставьте токен администратора.
7. Создайте событие с датами в будущем, например:

   ```json
   {
     "title": "Встреча команды",
     "description": "Проверка восьмого спринта",
     "startAt": "2099-11-01T12:00:00Z",
     "endAt": "2099-11-01T13:00:00Z",
     "totalSeats": 20
   }
   ```

   Ожидается 201 и Location.
8. Через `POST /events/{id}/book` создайте бронь: 202, статус Pending,
   UserId соответствует текущему пользователю. Через несколько секунд фоновый сервис
   переведёт её в Confirmed.
9. Проверьте `GET /bookings/{id}`, затем `DELETE /bookings/{id}`: 204.
   Повторный GET показывает Cancelled, свободных мест снова 20.
   Повторная отмена возвращает 204, но не увеличивает места второй раз.
10. Для проверки прав создайте бронь обычным пользователем. Другой User получает
    403 при её отмене; владелец или Admin получает 204.
11. Создайте 10 активных броней одним пользователем: следующая возвращает 409
    с числом 10 в описании. Другой пользователь всё ещё может бронировать.
12. Перезапустите API: пользователи, события, брони и статусы сохраняются.

## Валидация и ошибки

- Название обязательно, не более 200 символов.
- Описание необязательно, не более 2000 символов.
- Начало и окончание обязательны; окончание строго позже начала.
- Даты хранятся в UTC. Используйте ISO 8601 с `Z` или смещением;
  дата без смещения трактуется как UTC.
- Общее количество мест положительное.
- При обновлении нельзя уменьшить вместимость ниже уже занятого числа мест.

Ошибки возвращаются в формате Problem Details:
`400` — валидация, неверный вход или событие началось; `401` — нет действующего токена;
`403` — нет прав; `404` — ресурс отсутствует; `409` — нет мест или превышен лимит;
`500` — непредвиденная ошибка. Автоматическая валидация DTO дополнительно
возвращает `errors` в Validation Problem Details.

Middleware явно задаёт `Content-Type: application/problem+json; charset=utf-8`.
Для ожидаемых ошибок сохраняется понятное описание; при `500` клиент получает
нейтральное сообщение, подробности исключения остаются в журнале сервера.
Отмена запроса клиентом не преобразуется в `500`, уже отправленный ответ не перезаписывается.

## Работа с данными и конкурентностью

Контроллеры обращаются к сервисам, сервисы — к `IEventRepository` и
`IBookingRepository` и `IUserRepository`, репозитории — к `AppDbContext`.
Сервисы не выполняют запросы EF Core и не содержат in-memory хранилищ.
Репозитории отвечают за доступ к данным, правила бронирования остаются в сервисах и моделях.

`AppDbContext`, `IUnitOfWork`, репозитории, `EventService`, `BookingService` и `UserService`
зарегистрированы как Scoped и разделяют один контекст внутри запроса.
Для обычного чтения применяется `AsNoTracking`; методы `GetByIdForUpdateAsync`
возвращают отслеживаемые сущности. Репозитории только подготавливают изменения.
Сервисы явно вызывают `IUnitOfWork.SaveChangesAsync()` после успешной операции;
реализация `UnitOfWork` сохраняет общий `AppDbContext` одной транзакцией.
Fluent API конфигурации автоматически подключаются из сборки.
Идентификаторы создаются в коде (`ValueGeneratedNever`), статус брони хранится строкой.
Связь: одно событие — много броней. Удаление события каскадно удаляет его брони.

Создание брони и уменьшение свободных мест сохраняются одной транзакцией PostgreSQL.
Статический `SemaphoreSlim` общий для scoped-сервисов: он защищает
бронирование, изменение вместимости, удаление события и возврат мест внутри
одного процесса. Для нескольких экземпляров API потребуется отдельное решение
конкурентности на уровне БД — это ограничение учебной реализации.

Фоновый сервис через `IServiceScopeFactory` получает список Id ожидающих броней.
Для каждой обработки создаётся новый scope и контекст. Параллельно обрабатываются
до восьми броней, задержка внешнего вызова имитируется двумя секундами.
Фоновый сервис не хранит DbContext или сущности между итерациями.
При остановке или временной ошибке необработанные брони остаются `Pending`
и подбираются при следующем запуске.

Переход из `Pending` в `Rejected` через сервис атомарно возвращает место.
Повторная обработка окончательного статуса ничего не меняет.

## Тесты

Для полного прогона запустите Docker. Базу из `docker-compose.yml` для самих
тестов поднимать необязательно: Testcontainers создаёт отдельный контейнер.
При первом запуске нужен доступ к реестру образов Docker и NuGet для восстановления пакетов.

```bash
dotnet test
```

В решении три тестовых проекта, всего 302 теста:

- `ProjectWork.Tests` — 178 юнит- и архитектурных тестов, Docker не нужен.
  Проверяются доменные правила Create/Update, UTC, сервисы, конкурентность,
  фоновая обработка и направление зависимостей сборок.
  Дополнительно проверяются регистрация Infrastructure, Scoped lifetime, фоновый
  сервис, настройки PostgreSQL и обнаружение миграций без соединения с БД.
  Также проверяются регистрация Application и отсутствие зависимости тестов от Presentation.
  Дополнительно покрыты пользователи, SHA-256, JWT-подпись и claims, отмена,
  начало события, лимит 10 и конкурентные запросы. Время бизнес-правил фиксировано.
  Для тестов доступа к данным используется EF Core InMemory.
  `TestDatabase` создаёт уникальную БД на экземпляр тестового класса;
  каждый конкурентный запрос использует собственный scope и контекст.
- `ProjectWork.IntegrationTests` — 90 интеграционных тестов с настоящей PostgreSQL 16
  через Testcontainers. Проверяются все методы обоих репозиториев, фильтры,
  пагинация, обновление, удаление, транзакции, миграции и ограничения БД.
- `ProjectWork.PresentationTests` — 34 теста: 13 контроллеров/middleware на InMemory, 4 проверки конфигурации
  и 17 HTTP-проверок настоящего конвейера с PostgreSQL, Docker нужен.
  Проверяются DTO-контракты, статусы и Location, маппинг исключений, безопасный `500`,
  отмена запроса и запрет перезаписи начатого ответа. Фикстура использует отдельную
  InMemory-базу. HTTP-тесты используют отдельный контейнер и WebApplicationFactory:
  проверяют JWT, роли, 401/403, отмену, лимит, валидацию и Swagger.

```bash
# Только юнит-тесты, без Docker
dotnet test ProjectWork.Tests/ProjectWork.Tests.csproj

# HTTP-слой: InMemory и настоящий конвейер с PostgreSQL, нужен Docker
dotnet test ProjectWork.PresentationTests/ProjectWork.PresentationTests.csproj

# Только направление зависимостей сборок
dotnet test ProjectWork.Tests/ProjectWork.Tests.csproj --filter FullyQualifiedName~ArchitectureTests

# Только интеграционные тесты, Docker обязателен
dotnet test ProjectWork.IntegrationTests/ProjectWork.IntegrationTests.csproj

# Выбор интеграционных тестов по категории
dotnet test --filter "Category=Integration"
```

Фикстуры `IAsyncLifetime` поднимают отдельные PostgreSQL-контейнеры для репозиториев и HTTP API.
В коллекции репозиториев перед каждым тестом временная БД пересоздаётся через
`EnsureDeletedAsync()` и `MigrateAsync()`; тесты коллекции не выполняются параллельно.
HTTP-коллекция использует собственную БД и уникальные логины и события для каждого сценария. Рабочая БД из
`appsettings.json` не затрагивается. После прогона контейнер удаляется.
Подробности: [интеграционные тесты](ProjectWork.IntegrationTests/README.md).

После сборки можно выполнить автоматическую HTTP-проверку реального PostgreSQL:

```powershell
pwsh -File scripts/Verify-Api.ps1
```

Для сценария нужны Docker и собранный API. Скрипт сам создаёт временный PostgreSQL,
переопределяет строку подключения только для своего процесса API и удаляет контейнер в конце.
Для проверки скрипт генерирует отдельный случайный JWT-ключ и восстанавливает переменные окружения в конце.
Рабочая база не используется. API запускается на `127.0.0.1:5260`.
Проверяются все 10 операций Swagger, JWT, роли, 401/403, отмена, лимит, CRUD,
валидация, конкурентность и перезапуск. HTTP-проверка не заменяет ручной сценарий
с кнопками Swagger, описанный выше.
Для Release-сборки добавьте `-Configuration Release`.

## Структура

```text
ProjectWork.Presentation/Controllers/ HTTP-эндпоинты
ProjectWork.Domain/Entities/ Event, Booking, User, роли и статусы
ProjectWork.Domain/Exceptions/ Доменные исключения
ProjectWork.Application/Abstractions/IUnitOfWork.cs Порт атомарного сохранения
ProjectWork.Application/Abstractions/Security/ Порты хеширования и генерации JWT
ProjectWork.Application/Abstractions/Repositories/ Интерфейсы репозиториев
ProjectWork.Application/Services/ Сервисы, их интерфейсы и общий семафор записи
ProjectWork.Application/DTO/ Контракты запросов и ответов
ProjectWork.Application/Common/ PaginatedResult<T>
ProjectWork.Application/DependencyInjection.cs Регистрация прикладных сервисов
ProjectWork.Infrastructure/Persistence/ AppDbContext
ProjectWork.Infrastructure/Persistence/UnitOfWork.cs Атомарное сохранение изменений
ProjectWork.Infrastructure/Persistence/Configurations/ Маппинг через Fluent API
ProjectWork.Infrastructure/Persistence/Migrations/ InitialCreate, пользователи и снимок модели
ProjectWork.Infrastructure/Repositories/ EF Core-реализации репозиториев
ProjectWork.Infrastructure/Security/ SHA-256, JWT и параметры токена
ProjectWork.Infrastructure/BackgroundServices/ Фоновая обработка бронирований
ProjectWork.Infrastructure/DependencyInjection.cs Регистрация инфраструктуры
ProjectWork.Infrastructure/DatabaseInitialization.cs Применение миграций в отдельном scope
ProjectWork.Presentation/Middleware/ Преобразование исключений в Problem Details
ProjectWork.Tests/          xUnit и EF Core InMemory
ProjectWork.IntegrationTests/ xUnit и PostgreSQL через Testcontainers
ProjectWork.PresentationTests/ Контроллеры и middleware, xUnit и InMemory
scripts/                    Проверка API с настоящей PostgreSQL
docker-compose.yml          Локальная PostgreSQL
ProjectWork.Presentation/DependencyInjection.cs Настройки контроллеров, JSON, Problem Details и Swagger
ProjectWork.Presentation/Program.cs Composition root и HTTP pipeline
```
