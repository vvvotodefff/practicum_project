namespace ProjectWork.Application.Services;

// Все операции, меняющие места, используют один семафор между scoped-сервисами.
// Защита действует внутри одного процесса приложения, не между несколькими серверами.
internal static class EventWriteLock
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);
}
