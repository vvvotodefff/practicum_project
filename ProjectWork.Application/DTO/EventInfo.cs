using ProjectWork.Domain.Entities;

namespace ProjectWork.Application.DTO
{
    /// <summary>
    /// Информация о событии, которую возвращает API
    /// </summary>
    public class EventInfo
    {
        /// <summary>
        /// Уникальный идентификатор события
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Название события
        /// </summary>
        public required string Title { get; set; }

        /// <summary>
        /// Описание события
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Время начала события
        /// </summary>
        public DateTime StartAt { get; set; }

        /// <summary>
        /// Время окончания события
        /// </summary>
        public DateTime EndAt { get; set; }

        /// <summary>
        /// Общее количество мест на событии
        /// </summary>
        public int TotalSeats { get; set; }

        /// <summary>
        /// Текущее количество свободных мест
        /// </summary>
        public int AvailableSeats { get; set; }

        /// <summary>
        /// Собрать DTO из сущности события
        /// </summary>
        public static EventInfo FromEvent(Event eventItem) => new()
        {
            Id = eventItem.Id,
            Title = eventItem.Title,
            Description = eventItem.Description,
            StartAt = eventItem.StartAt,
            EndAt = eventItem.EndAt,
            TotalSeats = eventItem.TotalSeats,
            AvailableSeats = eventItem.AvailableSeats
        };
    }
}
