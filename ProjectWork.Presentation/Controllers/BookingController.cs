using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ProjectWork.Presentation.Authentication;
using ProjectWork.Domain.Exceptions;
using ProjectWork.Application.DTO;
using ProjectWork.Application.Services;

namespace ProjectWork.Presentation.Controllers
{
    /// <summary>
    /// Бронирование мест на событиях
    /// </summary>
    [ApiController]
    [Authorize]
    public class BookingController : ControllerBase
    {
        private readonly IBookingService _bookingService;

        /// <summary>
        /// Создаёт контроллер бронирований
        /// </summary>
        public BookingController(IBookingService bookingService)
        {
            _bookingService = bookingService;
        }

        /// <summary>
        /// Создать бронь для события. Бронь принимается в обработку и получает статус Pending
        /// </summary>
        /// <param name="id">Идентификатор события</param>
        /// <returns></returns>
        /// <response code="202">Бронь принята к обработке, ссылка на неё — в заголовке</response>
        /// <response code="404">Событие с указанным ID не найдено</response>
        /// <response code="409">На событии не осталось свободных мест</response>
        [HttpPost("events/{id:guid}/book")]
        [ProducesResponseType(typeof(BookingInfo), StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<BookingInfo>> BookEvent(Guid id)
        {
            var booking = await _bookingService.CreateBookingAsync(id, CurrentUser.Id(User));

            return AcceptedAtAction(nameof(GetBookingById), new { id = booking.Id },
                booking);
        }

        /// <summary>
        /// Получить текущее состояние брони по её уникальному идентификатору ID
        /// </summary>
        /// <param name="id">Идентификатор брони</param>
        /// <returns></returns>
        /// <response code="200">Успешно возвращает бронь с указанным ID</response>
        /// <response code="404">Бронь с указанным ID не найдена</response>
        [HttpGet("bookings/{id:guid}")]
        [ProducesResponseType(typeof(BookingInfo), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<BookingInfo>> GetBookingById(Guid id)
        {
            var booking = await _bookingService.GetBookingByIdAsync(id);

            if (booking is null)
                throw new NotFoundException($"Бронь с идентификатором '{id}' не найдена.");

            return Ok(booking);
        }
        /// <summary>Отменить свою бронь; администратор может отменить любую.</summary>
        [HttpDelete("bookings/{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CancelBooking(Guid id, CancellationToken cancellationToken)
        {
            await _bookingService.CancelBookingAsync(id, CurrentUser.Id(User), CurrentUser.Role(User), cancellationToken);
            return NoContent();
        }
    }
}
