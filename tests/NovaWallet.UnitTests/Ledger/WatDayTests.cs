using NovaWallet.Domain.Ledger;

namespace NovaWallet.UnitTests.Ledger;

public class WatDayTests
{
    private static readonly TimeSpan Wat = TimeSpan.FromHours(1);

    [Fact]
    public void Day_runs_from_WAT_midnight_to_the_next_WAT_midnight()
    {
        var day = WatDay.Containing(new DateTimeOffset(2026, 10, 16, 15, 0, 0, Wat));

        Assert.Equal(new DateTimeOffset(2026, 10, 16, 0, 0, 0, Wat), day.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 0, 0, 0, Wat), day.End);
    }

    [Fact]
    public void Last_millisecond_belongs_to_the_day_and_midnight_to_the_next()
    {
        var day = WatDay.Containing(new DateTimeOffset(2026, 10, 16, 12, 0, 0, Wat));

        Assert.True(day.Contains(new DateTimeOffset(2026, 10, 16, 23, 59, 59, 999, Wat)));
        Assert.False(day.Contains(new DateTimeOffset(2026, 10, 17, 0, 0, 0, Wat)));
    }

    [Fact]
    public void Boundaries_are_utc_so_the_database_driver_accepts_them()
    {
        var day = WatDay.Containing(new DateTimeOffset(2026, 10, 16, 15, 0, 0, Wat));

        Assert.Equal(TimeSpan.Zero, day.Start.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 23, 0, 0, TimeSpan.Zero), day.Start);
    }

    [Fact]
    public void Utc_instants_after_2300_belong_to_the_next_WAT_day()
    {
        var day = WatDay.Containing(new DateTimeOffset(2026, 10, 16, 23, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 10, 17, 0, 0, 0, Wat), day.Start);
    }
}
