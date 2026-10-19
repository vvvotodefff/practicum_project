namespace ProjectWork.Bookings.Application.Services;

// Сериализует изменения в одном экземпляре сервиса, в том числе проверки лимита.
// Для нескольких реплик понадобится блокировка/изоляция на уровне PostgreSQL.
internal static class BookingWriteLock
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);
}
