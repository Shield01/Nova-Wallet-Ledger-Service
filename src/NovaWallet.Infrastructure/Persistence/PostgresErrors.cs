using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NovaWallet.Infrastructure.Persistence;

internal static class PostgresErrors
{
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
