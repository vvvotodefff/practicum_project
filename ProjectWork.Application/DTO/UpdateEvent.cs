using System.ComponentModel.DataAnnotations;

namespace ProjectWork.Application.DTO
{
    /// <summary>
    /// Данные для обновления события. Количество свободных мест сервер считает сам
    /// </summary>
    public class UpdateEvent
    {
        /// <summary>
        /// Название события
        /// </summary>
        [Required(ErrorMessage = "Название обязательно для заполнения")]
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
        /// Общее количество мест. Нельзя сделать меньше, чем уже занято
        /// </summary>
        [Required(ErrorMessage = "Количество мест обязательно для заполнения")]
        public int? TotalSeats { get; set; }
    }
}
