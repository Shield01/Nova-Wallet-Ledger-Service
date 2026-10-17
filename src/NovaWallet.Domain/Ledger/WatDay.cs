namespace NovaWallet.Domain.Ledger;

/// <summary>
/// The West Africa Time calendar day: a fixed UTC+01:00 offset (Nigeria has no daylight saving),
/// as the half-open interval [00:00, next 00:00). Boundaries are returned in UTC, because that is
/// how instants are stored and compared.
/// </summary>
public readonly record struct WatDay(DateTimeOffset Start, DateTimeOffset End)
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(1);

    public static WatDay Containing(DateTimeOffset instant)
    {
        var local = instant.ToOffset(Offset);
        var start = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, Offset).ToUniversalTime();
        return new WatDay(start, start.AddDays(1));
    }

    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;
}
