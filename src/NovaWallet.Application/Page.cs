namespace NovaWallet.Application;

public sealed record Page<T>(IReadOnlyList<T> Items, PageCursor? Next);

public static class Page
{
    /// <summary>Builds a page from up to <paramref name="limit"/> + 1 rows; the extra row signals another page.</summary>
    public static Page<T> From<T>(IReadOnlyList<T> rows, int limit, Func<T, PageCursor> cursorOf)
    {
        var items = rows.Take(limit).ToList();
        var next = rows.Count > limit ? cursorOf(items[^1]) : null;
        return new Page<T>(items, next);
    }
}
