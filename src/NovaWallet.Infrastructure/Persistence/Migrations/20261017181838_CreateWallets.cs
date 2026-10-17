using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaWallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateWallets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wallets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    balance_kobo = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.id);
                    table.CheckConstraint("ck_wallets_customer_balance_not_negative", "kind = 'SYSTEM' OR balance_kobo >= 0");
                    table.CheckConstraint("ck_wallets_kind_matches_customer", "(kind = 'SYSTEM' AND customer_id IS NULL) OR (kind = 'CUSTOMER' AND customer_id IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_wallets_customer_id",
                table: "wallets",
                column: "customer_id",
                unique: true);

            // The source of every credit. Its id is fixed so code and SQL can refer to it.
            migrationBuilder.Sql("""
                INSERT INTO wallets (id, customer_id, kind, balance_kobo, created_at)
                VALUES ('00000000-0000-0000-0000-000000000001', NULL, 'SYSTEM', 0, now());
                """);

            // The runtime may open wallets and change balances, nothing else: it can't reassign a
            // wallet, change its kind (escaping the non-negative check) or delete it.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON wallets TO novawallet_app;");
            migrationBuilder.Sql("GRANT UPDATE (balance_kobo) ON wallets TO novawallet_app;");

            // There is exactly one system account, with the fixed id.
            migrationBuilder.Sql("""
                ALTER TABLE wallets ADD CONSTRAINT ck_wallets_single_system_account
                    CHECK (kind <> 'SYSTEM' OR id = '00000000-0000-0000-0000-000000000001');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallets");
        }
    }
}
