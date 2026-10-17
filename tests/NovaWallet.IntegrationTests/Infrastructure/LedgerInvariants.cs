using Npgsql;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>The bookkeeping rules that must hold after any money movement, checked straight in SQL.</summary>
public static class LedgerInvariants
{
    public static async Task AssertHoldAsync(this NovaWalletApiFactory factory)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();

        Assert.Empty(await RowsAsync(connection, "SELECT transaction_id::text FROM ledger_entries GROUP BY transaction_id HAVING sum(amount_kobo) <> 0"));
        Assert.Empty(await RowsAsync(connection, """
            SELECT w.id::text FROM wallets w LEFT JOIN ledger_entries e ON e.wallet_id = w.id
            GROUP BY w.id, w.balance_kobo HAVING w.balance_kobo <> coalesce(sum(e.amount_kobo), 0)
            """));
        Assert.Empty(await RowsAsync(connection, "SELECT id::text FROM wallets WHERE kind = 'CUSTOMER' AND balance_kobo < 0"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT coalesce(sum(balance_kobo), 0)::bigint FROM wallets"));
        Assert.Empty(await RowsAsync(connection, """
            SELECT t.id::text FROM ledger_transactions t LEFT JOIN ledger_entries e ON e.transaction_id = t.id
            GROUP BY t.id HAVING count(e.id) <> 2
            """));
    }

    private static async Task<List<string>> RowsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
