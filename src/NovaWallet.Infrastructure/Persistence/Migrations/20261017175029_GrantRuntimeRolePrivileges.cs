using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaWallet.Infrastructure.Persistence.Migrations;

/// <summary>
/// Gives the runtime role access to every table and sequence later migrations create. Migrations
/// run as the schema owner, so default privileges apply to everything the owner creates. The role
/// itself is created outside migrations (deploy/db/init), because its password is a secret.
/// </summary>
public partial class GrantRuntimeRolePrivileges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'novawallet_app') THEN
                    GRANT USAGE ON SCHEMA public TO novawallet_app;
                    ALTER DEFAULT PRIVILEGES IN SCHEMA public
                        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO novawallet_app;
                    ALTER DEFAULT PRIVILEGES IN SCHEMA public
                        GRANT USAGE, SELECT ON SEQUENCES TO novawallet_app;
                ELSE
                    RAISE EXCEPTION 'Role novawallet_app must exist before migrations run';
                END IF;
            END
            $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'novawallet_app') THEN
                    ALTER DEFAULT PRIVILEGES IN SCHEMA public
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM novawallet_app;
                    ALTER DEFAULT PRIVILEGES IN SCHEMA public
                        REVOKE USAGE, SELECT ON SEQUENCES FROM novawallet_app;
                    REVOKE USAGE ON SCHEMA public FROM novawallet_app;
                END IF;
            END
            $$;
            """);
    }
}
