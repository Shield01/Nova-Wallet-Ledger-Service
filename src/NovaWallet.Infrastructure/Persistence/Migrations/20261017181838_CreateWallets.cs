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

            // Balances change, so the runtime role may update wallets, but never delete them.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON wallets TO novawallet_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallets");
        }
    }
}
