using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProjectWork.Application.DTO;
using ProjectWork.Application.Services;

namespace ProjectWork.Presentation.Controllers;

/// <summary>Регистрация и получение токена.</summary>
[ApiController]
[Route("auth")]
[AllowAnonymous]
public class AuthController(IUserService users) : ControllerBase
{
    /// <summary>Зарегистрировать пользователя. В учебном API разрешён выбор роли Admin.</summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterUser request, CancellationToken cancellationToken)
    {
        await users.RegisterAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Получить JWT по логину и паролю.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TokenInfo>> Login(LoginUser request, CancellationToken cancellationToken) =>
        Ok(await users.LoginAsync(request, cancellationToken));
}
