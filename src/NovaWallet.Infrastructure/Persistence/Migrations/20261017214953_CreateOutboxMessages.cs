using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaWallet.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateOutboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_attempts_not_negative", "attempts >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unpublished",
                table: "outbox_messages",
                columns: new[] { "occurred_at", "id" },
                filter: "published_at IS NULL");

            // The dispatcher only ever records delivery: the event itself can't be rewritten.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON outbox_messages TO novawallet_app;");
            migrationBuilder.Sql("GRANT UPDATE (published_at, attempts, last_error) ON outbox_messages TO novawallet_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages");
        }
    }
}
