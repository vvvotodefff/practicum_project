using System.Security.Claims;
using ProjectWork.Domain.Entities;

namespace ProjectWork.Presentation.Authentication;

/// <summary>Чтение проверенной JWT-идентичности без зависимости Application от HTTP.</summary>
public static class CurrentUser
{
    /// <summary>Получить идентификатор из claim sub.</summary>
    public static Guid Id(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue("sub")!);

    /// <summary>Получить роль из claim role.</summary>
    public static UserRole Role(ClaimsPrincipal principal) =>
        Enum.Parse<UserRole>(principal.FindFirstValue("role")!);
}
