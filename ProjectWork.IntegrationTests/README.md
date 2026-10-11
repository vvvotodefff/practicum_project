# Интеграционные тесты PostgreSQL

Для запуска нужны .NET SDK 10 и работающий Docker с Linux-контейнерами.
При первом запуске нужен доступ к реестру образов Docker.

Из корня решения:

```shell
dotnet test
dotnet test ProjectWork.IntegrationTests/ProjectWork.IntegrationTests.csproj
```

Тесты помечены `Category=Integration`; их можно выбрать через
`dotnet test --filter "Category=Integration"`.

Проект напрямую ссылается на Domain, Application и Infrastructure, без зависимости
от веб-проекта. Контекст, репозитории и миграции загружаются из Infrastructure.

## Изоляция

- `PostgresFixture` реализует `IAsyncLifetime` и поднимает один
  PostgreSQL 16 на всю коллекцию тестов. По завершении контейнер удаляется.
- Порт выбирает Testcontainers; строка подключения берётся из
  `GetConnectionString()`. Настройки рабочей базы из API не используются.
- Перед каждым тестом (включая строки `Theory`) выполняются
  `EnsureDeletedAsync()` и `MigrateAsync()` только для временной базы контейнера.
  `EnsureCreated` не используется.
- Классы входят в одну непараллельную коллекцию. У каждого теста свой scope;
  отдельные scope для чтения подтверждают, что данные действительно сохранены.
- Фоновые сервисы API не запускаются и не меняют данные тестов.

## Покрытие

- `EventRepositoryTests`: все методы репозитория, CRUD, отсутствие записей,
  отслеживание и обновление сущностей, каждый набор фильтров, границы дат,
  регистр названия, стабильная сортировка, страницы и общий счётчик.
- `BookingRepositoryTests`: все методы репозитория, выборки по статусам,
  tracked/untracked-чтение, добавление и обновление, общий контекст двух
  репозиториев, явное сохранение через `IUnitOfWork` и откат транзакции при ошибке.
- `MigrationTests`: создание и повторное применение миграций, таблицы,
  обязательные столбцы, первичные/внешний ключи, каскадное удаление на стороне БД,
  строковый статус, ограничения длины, дат и количества мест.

Подход к общему контейнеру основан на
[collection fixtures xUnit](https://xunit.net/docs/shared-context#collection-fixtures)
и [PostgreSQL-модуле Testcontainers](https://dotnet.testcontainers.org/modules/postgres/).
