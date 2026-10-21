# ProjectWork — спринт 10

Учебная система бронирования на C# / .NET 10 / ASP.NET Core / EF Core / PostgreSQL / Apache Kafka.
Монолит разделён на три самостоятельных сервиса, которые обмениваются данными только через Kafka.
Репозиторий: [practicum_project](https://github.com/vvvotodefff/practicum_project).

## Сервисы

| Сервис | Ответственность | Swagger | Своя PostgreSQL (с хоста) |
| --- | --- | --- | --- |
| Users | Регистрация, вход, выдача JWT | http://localhost:5250/swagger | localhost:5441 / users |
| Events | CRUD событий, учёт свободных мест, подписчик Kafka | http://localhost:5251/swagger | localhost:5442 / events |
| Bookings | Создание, чтение, отмена и фоновое подтверждение броней, издатель Kafka | http://localhost:5252/swagger | localhost:5443 / bookings |

Инфраструктура: Zookeeper, Kafka (`localhost:9092` с хоста, `kafka:29092` внутри сети Docker),
Redis — кеш Events (`localhost:6379` с хоста, `redis:6379` внутри сети Docker).

Каждый сервис построен по чистой архитектуре (Domain, Application, Infrastructure,
Presentation), имеет свою базу и свои миграции EF Core. Сервисы не вызывают друг
друга по HTTP и не читают чужие таблицы: Bookings хранит только внешние
идентификаторы EventId и UserId, без навигаций и внешних ключей.

## Запуск

### Вся система в Docker

Нужен Docker Desktop с Linux-контейнерами. В корне репозитория:

```powershell
docker compose up -d --build
```

Поднимаются Zookeeper, Kafka, Redis, три PostgreSQL и три сервиса. Каждый сервис при старте
применяет свои миграции; Events дополнительно создаёт топик `booking-confirmed`.
Сервисы ждут готовности своих БД и Kafka (healthcheck).

Остановка без удаления данных — `docker compose down`, с удалением томов — `docker compose down -v`.

Настройки сервисов в контейнерах передаются переменными окружения из
[docker-compose.yml](docker-compose.yml) (`Секция__Ключ` переопределяет `appsettings.json`):
строки подключения указывают на `users-db`/`events-db`/`bookings-db`, Kafka — на `kafka:29092`.
JWT-секрет общий для трёх сервисов; по умолчанию используется учебное значение,
своё можно задать переменной `JWT_SECRET` перед `docker compose up`.

Образы собираются многоступенчатыми Dockerfile (`Services/<Сервис>/Dockerfile`, контекст —
корень репозитория): SDK для restore/publish, `aspnet:10.0` для запуска от непривилегированного пользователя.

### Сервисы локально

Нужен .NET SDK 10. Поднимите только инфраструктуру и запустите сервисы из трёх терминалов:

```powershell
docker compose up -d --wait zookeeper kafka redis users-db events-db bookings-db
```

```powershell
dotnet run --project Services/Users/ProjectWork.Users.Presentation --launch-profile http
```

```powershell
dotnet run --project Services/Events/ProjectWork.Events.Presentation --launch-profile http
```

```powershell
dotnet run --project Services/Bookings/ProjectWork.Bookings.Presentation --launch-profile http
```

Профиль `http` использует окружение Development: учебный JWT-ключ берётся из
`appsettings.Development.json`, БД — `localhost:5441–5443` (postgres / postgres),
Kafka — `localhost:9092`. В базовом `appsettings.json` секрет пуст, поэтому
Production без явно заданного `Jwt__Secret` не запустится.
В Visual Studio откройте ProjectWork.slnx и назначьте три проекта Presentation запускаемыми.

### Настройки

| Ключ | Сервисы | Назначение |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | все | Своя база сервиса |
| `Jwt:Secret`, `Jwt:Issuer`, `Jwt:Audience` | все | Должны совпадать во всех трёх сервисах |
| `Jwt:LifetimeMinutes` | Users | Время жизни выдаваемого токена |
| `Kafka:BootstrapServers` | Events, Bookings | Адрес брокера |
| `Kafka:ConsumerGroup` | Events | Группа потребителей (`events-group`) |
| `Redis:ConnectionString` | Events | Строка подключения StackExchange.Redis |
| `Redis:EventTtl`, `Redis:TopEventsTtl` | Events | TTL кеша события и топа-10 (`чч:мм:сс`) |

## Поток BookingConfirmed

```mermaid
sequenceDiagram
    participant C as Клиент
    participant B as Bookings
    participant BDB as bookings
    participant K as Kafka: booking-confirmed
    participant E as Events
    participant EDB as events
    C->>B: POST /events/{id}/book (JWT)
    B->>BDB: бронь Pending
    B-->>C: 202 Accepted
    Note over B: фоновый BookingProcessingService
    B->>BDB: бронь Confirmed (SaveChanges)
    B->>K: BookingConfirmed, ключ = EventId
    K->>E: BookingConfirmedConsumer
    E->>EDB: AvailableSeats -= SeatsCount + отметка в processed_bookings
```

**Кто публикует.** Bookings. Фоновый `BookingProcessingService` переводит брони из Pending
в Confirmed, сначала сохраняет статус в свою базу и только после этого публикует
сообщение через `IBookingEventPublisher` (реализация — `KafkaBookingEventPublisher`).
Producer один на приложение (singleton), при остановке выполняет Flush и Dispose.
Ключ сообщения — EventId: все брони одного события попадают в один раздел и
обрабатываются по порядку. Bookings не меняет места и не обращается к Events.

**Кто получает.** Events. `BookingConfirmedConsumer` (BackgroundService) подписан на топик
в группе `events-group`: при нескольких экземплярах Events каждое сообщение получает
только один из них. Блокирующий `Consume` работает в отдельной задаче; на каждое
сообщение создаётся свой scope для DbContext и репозиториев. До запуска подписчика
`KafkaTopicInitializer` создаёт топик (3 раздела), если его ещё нет; ошибка создания
только логируется и не роняет сервис.

**Что происходит при получении.** Сообщение десериализуется в `BookingConfirmed`,
и `EventService.ApplyBookingConfirmedAsync` уменьшает `AvailableSeats` события на
`SeatsCount`. Одновременно в таблицу `processed_bookings` записывается BookingId — в одной
операции SaveChanges с изменением мест. Поэтому:

- повторная доставка того же сообщения места повторно не списывает (идемпотентность);
- событие не найдено или мест не хватает — сообщение пропускается с предупреждением в логе;
- некорректный JSON или пустые поля — пропуск с предупреждением, подписчик не падает;
- сбой БД — сообщение не подтверждается и читается повторно через 5 секунд.

Смещение в Kafka сохраняется только после обработки сообщения.

Формат сообщения и топик описаны в общем проекте
[Shared/ProjectWork.Contracts](Shared/ProjectWork.Contracts/README.md), который подключают и Bookings, и Events.

## API

| Сервис | Метод и маршрут | Доступ / результат |
| --- | --- | --- |
| Users | POST /auth/register | Публично, 204; ошибка 400 |
| Users | POST /auth/login | Публично, 200 с полем token; ошибка 400 |
| Events | GET /events | Публично, фильтры title/from/to, пагинация page/pageSize |
| Events | GET /events/{id} | Публично, 200 / 404; кешируется |
| Events | GET /events/top | Публично, 10 событий с наибольшей долей проданных мест; кешируется |
| Events | POST /events | Admin, 201 с Location |
| Events | PUT /events/{id} | Admin, 204 / 404 |
| Events | DELETE /events/{id} | Admin, 204 / 404 |
| Bookings | POST /events/{id}/book | Авторизованный пользователь, 202 с Location |
| Bookings | GET /bookings/{id} | Владелец или Admin; чужая и отсутствующая бронь дают 404 |
| Bookings | DELETE /bookings/{id} | Владелец или Admin, 204; чужая — 403 |

Создание брони отправляется **на порт Bookings (5252)**, хотя путь начинается с /events.

JWT выдаёт только Users; Events и Bookings проверяют подпись, срок, издателя, аудиторию
и claims (`sub` — идентификатор пользователя, `role` — User или Admin). Без токена — 401,
без роли Admin на управлении событиями — 403. Swagger каждого сервиса поддерживает
кнопку Authorize: вставьте token из `/auth/login` без префикса Bearer.

Лимит — 10 активных броней пользователя (Pending и Confirmed). Повторная отмена безопасна.
Title обязателен (до 200 символов), Description — до 2000, EndAt > StartAt, TotalSeats > 0.
Время хранится в UTC. Ошибки возвращаются в формате Problem Details.

## Проверка сценария

На запущенной в Docker системе:

```powershell
./scripts/Verify-Docker.ps1
```

Скрипт регистрирует Admin и User в Users, проверяет 403/401, создаёт событие на 5 мест
в Events, бронирует место в Bookings и ждёт, пока бронь станет Confirmed, а в Events
останется 4 места — уменьшение приходит только через Kafka.

То же вручную через Swagger: зарегистрируйтесь в Users (`role: Admin`), получите token,
создайте событие в Events, под токеном пользователя забронируйте его в Bookings,
через пару секунд проверьте статус брони и `availableSeats` события.

Посмотреть сообщения в топике:

```powershell
docker exec -it eventapi-kafka kafka-console-consumer --bootstrap-server localhost:29092 --topic booking-confirmed --from-beginning --property print.key=true
```

## Кеширование (Redis)

Events кеширует чтение в Redis по паттерну **Cache-Aside**: сервис сначала смотрит в кеш,
при промахе читает базу и кладёт результат в кеш с TTL. Redis спрятан за интерфейсом
`ICacheService` (Application), реализация — `RedisCacheService` (Infrastructure); ключи
собраны в `CacheKeys`, значения хранятся в JSON. Соединение `IConnectionMultiplexer` —
singleton на всё приложение.

| Ключ | Что лежит | Эндпоинт | TTL | Обновление |
| --- | --- | --- | --- | --- |
| `event:{id}` | Одно событие (`EventInfo`) | GET /events/{id} | `Redis:EventTtl`, 5 мин | Удаление при записи + TTL |
| `events:top10` | 10 событий с наибольшей долей проданных мест | GET /events/top | `Redis:TopEventsTtl`, 1 мин | Только TTL |

### Что и почему кешируется

- **Топ-10** (`GET /events/top`, публичный) запрашивает каждый посетитель главной страницы,
  а запрос сортирует все события по `(total_seats - available_seats) / total_seats`.
  Пересчитывать его на каждый запрос дорого, а результат один для всех.
- **Событие по идентификатору** читается намного чаще, чем меняется: каждую карточку
  открывают многие пользователи, а правят её администратор и подтверждения броней.

Список `GET /events` с фильтрами и пагинацией не кешируется: комбинаций параметров
слишком много, попаданий было бы мало, а инвалидировать пришлось бы всё.

### Стратегия обновления

**Событие — удаление при записи (invalidate on write).** После успешного сохранения в базу
сервис удаляет ключ `event:{id}`; следующий GET читает базу и прогревает кеш заново.
Ключ удаляется в `UpdateEventAsync` (PUT), `DeleteEventAsync` (DELETE) и
`ApplyBookingConfirmedAsync` — его вызывает подписчик Kafka `booking-confirmed`, когда
списывает места, поэтому `availableSeats` не залипает в кеше после брони. Если событие не
найдено или бронь уже была обработана, данные не менялись и кеш не трогается.
Создание события кеш не трогает: под новым id в кеше ничего нет.

Почему удаление, а не обновление кеша вместе с базой: оно проще и безопаснее — в кеш
никогда не попадёт значение, которого нет в базе, и не нужно повторять в кеше логику
пересчёта мест. Цена — один лишний запрос к базе после каждого изменения, для редких
изменений это незаметно.

**Порядок операций — сначала база, потом кеш.** Если процесс оборвётся между
`SaveChangesAsync` и удалением ключа, база уже актуальна, а устаревшая запись проживёт
не дольше TTL. Если сохранение в базу упало, ключ не удаляется — данные не изменились.

**Топ — только TTL.** Это рейтинговый агрегат, устаревание на минуту некритично. Явная
инвалидация при каждом бронировании и изменении любого события сбрасывала бы топ
постоянно и сводила кеш на нет.

### Выбор TTL

- `EventTtl` = 5 минут — страховка, а не основной механизм актуальности: изменения
  события снимает инвалидация. TTL ограничивает срок жизни устаревшей записи в редких
  случаях — сбой между записью в базу и удалением ключа, недоступный в момент записи
  Redis, гонка медленного читателя с записью (читатель взял из базы старое значение и
  положил его в кеш уже после удаления ключа).
- `TopEventsTtl` = 1 минута — единственный механизм обновления топа, поэтому короче:
  продажи отражаются в рейтинге не позже чем через минуту, а база при этом получает
  не больше одного тяжёлого запроса в минуту вместо запроса от каждого посетителя.

Оба значения и строка подключения задаются в `appsettings.json` (секция `Redis`) и
переопределяются переменными окружения (`Redis__ConnectionString`, `Redis__EventTtl`,
`Redis__TopEventsTtl`). В Docker сервис подключается к `redis:6379`.

### Если Redis недоступен

Кеш деградирует без ошибки для клиента: `RedisCacheService` ловит и логирует (Warning)
ошибки Redis — чтение возвращает промах и запрос идёт в базу, запись и удаление
пропускаются. `AbortOnConnectFail = false`, поэтому сервис стартует и без Redis,
а клиент сам переподключается, когда Redis вернётся. `BacklogPolicy.FailFast`: без
соединения команда сразу завершается ошибкой, а не ждёт в очереди до таймаута, — иначе
каждый запрос при упавшем Redis тормозил бы на секунды перед походом в базу.

Проверить вручную: `docker compose stop redis` — GET/PUT/DELETE событий и
`GET /events/top` продолжают работать, в логах `events-api` появляются предупреждения
`RedisCacheService`; после `docker compose start redis` кеш снова наполняется.
Содержимое кеша: `docker exec eventapi-redis redis-cli KEYS "event*"`, срок жизни —
`docker exec eventapi-redis redis-cli TTL events:top10`.

Риск: если Redis был недоступен в момент изменения события, а потом вернулся, в нём
может остаться старая копия — она истечёт по `EventTtl`.

## Ограничения

- Сохранение брони и публикация не составляют общей транзакции: при сбое между ними бронь
  останется Confirmed без сообщения. Гарантированная доставка требует Transactional Outbox.
- Bookings не знает, существует ли событие и есть ли места: бронь подтверждается сразу,
  а Events при нехватке мест только пропускает сообщение. Отказа брони и возврата мест
  при отмене нет — для этого нужны дополнительные сообщения.
- Удаление события не затрагивает брони в другой базе.
- Учебные упрощения из спринта 8: регистрация позволяет выбрать роль Admin, пароли хешируются
  SHA-256 без соли. Семафоры защищают изменения только внутри одного процесса.

## Структура

```text
Services/
  Users/     Domain, Application, Infrastructure, Presentation, Dockerfile
  Events/    Domain, Application, Infrastructure (+ Messaging: подписчик, создание топика; Caching: Redis), Presentation, Dockerfile
  Bookings/  Domain, Application, Infrastructure (+ Messaging: издатель), Presentation, Dockerfile
Shared/
  ProjectWork.Contracts/   BookingConfirmed и имя топика
ProjectWork.Tests/               unit-тесты
ProjectWork.IntegrationTests/    репозитории и миграции на PostgreSQL
ProjectWork.PresentationTests/   HTTP, JWT, Kafka
scripts/
docker-compose.yml
ProjectWork.slnx
```

Domain не зависит от других слоёв; Application зависит от своего Domain (Events и Bookings —
также от Contracts); Infrastructure реализует порты Application; Presentation подключает
свои Application и Infrastructure. Между сервисами нет ProjectReference.

## Миграции

- Users: InitialCreate — `users`.
- Events: InitialCreate — `events`; AddProcessedBookings — `processed_bookings` для идемпотентности.
- Bookings: InitialCreate — `bookings`, без FK в чужие базы.

Новая миграция (для других сервисов замените Users):

```powershell
dotnet ef migrations add MigrationName --project Services/Users/ProjectWork.Users.Infrastructure --startup-project Services/Users/ProjectWork.Users.Presentation --context UsersDbContext --output-dir Persistence/Migrations
```

## Тесты

```powershell
dotnet test ProjectWork.slnx --configuration Release --verbosity minimal
```

- ProjectWork.Tests: доменные правила, сервисы, JWT, зависимости слоёв, порядок
  сохранения и публикации, идемпотентное применение подтверждений в Events.
  `EventCachingTests` — кеширование Events на заглушках кеша и репозитория: попадание
  (репозиторий не вызывается), промах (данные из репозитория сохраняются с нужным TTL)
  и инвалидация после PUT, DELETE и подтверждения брони — строго после записи в базу.
- ProjectWork.IntegrationTests: репозитории и миграции каждого сервиса на PostgreSQL.
- ProjectWork.PresentationTests: три HTTP-конвейера, 401/403/404, совпадение JWT-настроек
  сервисов, Swagger. `KafkaPublicationTests` — публикация из Bookings на настоящем брокере;
  `KafkaSubscriptionTests` — Events на пустом брокере создаёт топик и списывает места
  ровно один раз, пропуская дубликат и некорректное сообщение.

Тесты сами поднимают временные контейнеры PostgreSQL и Kafka со случайными портами и
не трогают базы compose. Только Kafka-тесты: `--filter Category=Kafka`.
CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) собирает решение и запускает тесты
при push в main/sprint-* и PR в main.
