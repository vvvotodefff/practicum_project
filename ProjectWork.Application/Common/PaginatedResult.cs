namespace ProjectWork.Application.Common;

/// <summary>
/// Страница результатов с информацией о пагинации
/// </summary>
public class PaginatedResult<T>
{
    /// <summary>
    /// Общее количество элементов (с учётом фильтров, до разбиения на страницы)
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Номер текущей страницы
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Количество элементов на странице
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Элементы текущей страницы
    /// </summary>
    public List<T> Items { get; set; } = [];
}
