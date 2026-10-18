using System.Security.Claims;

namespace ProjectWork.Bookings.Presentation.Authentication;

/// <summary>Идентификатор из проверенного JWT.</summary>
public static class CurrentUser
{
    /// <summary>Получить идентификатор пользователя из claim sub.</summary>
    public static Guid Id(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
}
