using ProjectWork.Domain.Exceptions;

namespace ProjectWork.Domain.Entities;

public class Event
{
    private Event() { }

    public ICollection<Booking> Bookings { get; private set; } = new List<Booking>();

    public Guid Id { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    public int TotalSeats { get; set; }

    public int AvailableSeats { get; set; }



    /// <summary>
    /// Создать событие: проверяет данные, присваивает идентификатор
    /// и делает все места свободными. Бросает <see cref="DomainValidationException"/>,
    /// если данные некорректны
    /// </summary>
    public static Event Create(string title, string? description,
        DateTime startAt, DateTime endAt, int totalSeats)
    {
        startAt = ToUtc(startAt);
        endAt = ToUtc(endAt);
        var errors = GetErrors(title, description, startAt, endAt, totalSeats).ToList();

        if (errors.Count > 0)
            throw new DomainValidationException(string.Join(" ", errors.Select(e => e.Message)));

        return new Event
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            StartAt = startAt,
            EndAt = endAt,
            TotalSeats = totalSeats,
            AvailableSeats = totalSeats
        };
    }

    /// <summary>
    /// Обновить данные события. Количество свободных мест пересчитывается так,
    /// чтобы уже занятые места сохранились. Бросает <see cref="DomainValidationException"/>,
    /// если данные некорректны или новых мест меньше, чем уже занято
    /// </summary>
    public void Update(string title, string? description,
        DateTime startAt, DateTime endAt, int totalSeats)
    {
        startAt = ToUtc(startAt);
        endAt = ToUtc(endAt);
        var errors = GetErrors(title, description, startAt, endAt, totalSeats).ToList();

        if (errors.Count > 0)
            throw new DomainValidationException(string.Join(" ", errors.Select(e => e.Message)));

        var occupiedSeats = TotalSeats - AvailableSeats;

        if (totalSeats < occupiedSeats)
            throw new DomainValidationException(
                $"Нельзя установить {totalSeats} мест: уже занято {occupiedSeats}");

        Title = title;
        Description = description;
        StartAt = startAt;
        EndAt = endAt;
        TotalSeats = totalSeats;
        AvailableSeats = totalSeats - occupiedSeats;
    }

    // Единый набор доменных правил для создания и обновления события.
    private static IEnumerable<(string Field, string Message)> GetErrors(
        string? title, string? description, DateTime startAt, DateTime endAt, int totalSeats)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            yield return (nameof(Title), "Название обязательно для заполнения");
        }

        if (title?.Length > 200)
            yield return (nameof(Title), "Название не должно превышать 200 символов");

        if (description?.Length > 2000)
            yield return (nameof(Description), "Описание не должно превышать 2000 символов");

        if (startAt == default)
        {
            yield return (nameof(StartAt), "Время начала обязательно к заполнению");
        }

        if (endAt == default)
        {
            yield return (nameof(EndAt), "Время окончания обязательно к заполнению");
        }

        if (startAt != default &&
            endAt != default &&
            endAt <= startAt)
        {
            yield return (nameof(EndAt), "Время окончания должно быть позже времени начала");
        }

        if (totalSeats <= 0)
        {
            yield return (nameof(TotalSeats), "Общее количество мест должно быть положительным числом");
        }
    }

    /// <summary>Даты без смещения трактуются как UTC; локальные даты приводятся к UTC.</summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    public bool TryReserveSeats(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (AvailableSeats < count)
            return false;
        AvailableSeats -= count;
        return true;
    }

    /// <summary>
    /// Освободить места. Свободных мест не может стать больше, чем всего
    /// </summary>
    public void ReleaseSeats(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        AvailableSeats = (int)Math.Min((long)AvailableSeats + count, TotalSeats);
    }

}
