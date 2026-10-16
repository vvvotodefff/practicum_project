namespace ProjectWork.Application.Abstractions.Security;

/// <summary>Хеширование паролей и проверка их соответствия сохранённому хешу.</summary>
public interface IPasswordHasher
{
    /// <summary>Вычислить хеш пароля без изменения исходной строки.</summary>
    string Hash(string password);

    /// <summary>Проверить пароль; повреждённый хеш считается несовпадением.</summary>
    bool Verify(string password, string passwordHash);
}
