using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NovaWallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_kobo = table.Column<long>(type: "bigint", nullable: false),
                    balance_before_kobo = table.Column<long>(type: "bigint", nullable: false),
                    balance_after_kobo = table.Column<long>(type: "bigint", nullable: false),
                    actor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_wallet_wallet_id",
                        column: x => x.wallet_id,
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_occurred_at_id",
                table: "audit_log",
                columns: new[] { "occurred_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_wallet_id_occurred_at_id",
                table: "audit_log",
                columns: new[] { "wallet_id", "occurred_at", "id" },
                descending: new[] { false, true, true });

            // Append-only at the database level: no role, the owner included, may change or
            // remove audit rows. The owner isn't a superuser, so it can't bypass triggers with
            // session_replication_role either.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_log_reject_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_log is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END
                $$;

                CREATE TRIGGER audit_log_no_update_or_delete
                    BEFORE UPDATE OR DELETE ON audit_log
                    FOR EACH ROW EXECUTE FUNCTION audit_log_reject_change();

                CREATE TRIGGER audit_log_no_truncate
                    BEFORE TRUNCATE ON audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_log_reject_change();

                GRANT SELECT, INSERT ON audit_log TO novawallet_app;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_no_truncate ON audit_log;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_no_update_or_delete ON audit_log;");
            migrationBuilder.DropTable(
                name: "audit_log");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_log_reject_change();");
        }
    }
}
