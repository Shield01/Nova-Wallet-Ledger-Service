using System.Globalization;
using System.Text;

namespace NovaWallet.Application;

/// <summary>
/// Keyset position in a newest-first list: the timestamp and id of the last item returned.
/// Encoded as opaque base64url so clients don't build or edit cursors.
/// </summary>
public sealed record PageCursor(DateTimeOffset At, long Id)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public string Encode()
    {
        var raw = $"{At.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{Id.ToString(CultureInfo.InvariantCulture)}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? value, out PageCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split(':');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && ticks <= DateTimeOffset.MaxValue.UtcTicks)
            {
                cursor = new PageCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}
