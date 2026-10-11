using System.ComponentModel.DataAnnotations;

namespace ProjectWork.Application.DTO
{
    /// <summary>Данные для создания события; идентификатор и свободные места назначает сервер.</summary>
    public class CreateEvent
    {
        /// <summary>Обязательное название события.</summary>
        [Required(ErrorMessage = "Название обязательно для заполнения")]
        public required string Title { get; set; }

        /// <summary>Необязательное описание события.</summary>
        public string? Description { get; set; }

        /// <summary>Время начала события.</summary>
        public DateTime StartAt { get; set; }

        /// <summary>Время окончания; должно быть позже начала.</summary>
        public DateTime EndAt { get; set; }

        /// <summary>Общее положительное количество мест.</summary>
        [Required(ErrorMessage = "Количество мест обязательно для заполнения")]
        public int? TotalSeats { get; set; }
    }
}
