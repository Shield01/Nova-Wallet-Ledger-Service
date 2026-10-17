using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NovaWallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ledger_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount_kobo = table.Column<long>(type: "bigint", nullable: false),
                    source_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_transactions", x => x.id);
                    table.CheckConstraint("ck_ledger_transactions_amount_positive", "amount_kobo > 0");
                    table.ForeignKey(
                        name: "fk_ledger_transactions_wallet_destination_wallet_id",
                        column: x => x.destination_wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_transactions_wallet_source_wallet_id",
                        column: x => x.source_wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    amount_kobo = table.Column<long>(type: "bigint", nullable: false),
                    balance_after_kobo = table.Column<long>(type: "bigint", nullable: false),
                    counterparty_wallet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount_not_zero", "amount_kobo <> 0");
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_transaction_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "ledger_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_wallet_wallet_id",
                        column: x => x.wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_transaction_id",
                table: "audit_log",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_transaction_id",
                table: "ledger_entries",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_wallet_id_created_at_id",
                table: "ledger_entries",
                columns: new[] { "wallet_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_transactions_destination_wallet_id",
                table: "ledger_transactions",
                column: "destination_wallet_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_transactions_source_wallet_id",
                table: "ledger_transactions",
                column: "source_wallet_id");

            migrationBuilder.AddForeignKey(
                name: "fk_audit_log_ledger_transaction_transaction_id",
                table: "audit_log",
                column: "transaction_id",
                principalTable: "ledger_transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // The ledger is append-only for the runtime role: history is never edited or deleted.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON ledger_transactions, ledger_entries TO novawallet_app;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_audit_log_ledger_transaction_transaction_id",
                table: "audit_log");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "ledger_transactions");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_transaction_id",
                table: "audit_log");
        }
    }
}
