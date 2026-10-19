namespace ProjectWork.Events.Application.Services;

/// <summary>
/// Итог применения подтверждённой брони к событию
/// </summary>
public enum BookingConfirmationResult
{
    /// <summary>Места заняты.</summary>
    Applied,

    /// <summary>Бронь уже обработана ранее; места повторно не списываются.</summary>
    AlreadyProcessed,

    /// <summary>Событие не найдено.</summary>
    EventNotFound,

    /// <summary>Свободных мест не хватает.</summary>
    NoAvailableSeats
}
