#!/bin/sh
# Runs once, when the database volume is first initialised, as the image superuser.
# novawallet_owner owns the database and runs migrations but is not a superuser, so it can't
# switch off the audit triggers. novawallet_app is the runtime role; migrations grant it
# exactly the table privileges it needs.
set -eu

psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname postgres \
     -v owner_password="$NOVAWALLET_OWNER_PASSWORD" \
     -v app_password="$NOVAWALLET_APP_PASSWORD" <<'SQL'
CREATE ROLE novawallet_owner LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD :'owner_password';
CREATE ROLE novawallet_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD :'app_password';
ALTER DATABASE novawallet OWNER TO novawallet_owner;
SQL

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname novawallet <<'SQL'
ALTER SCHEMA public OWNER TO novawallet_owner;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
SQL
