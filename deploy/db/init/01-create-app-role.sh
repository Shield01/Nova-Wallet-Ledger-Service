#!/bin/sh
# Creates the least-privileged runtime role the API connects as. Runs once, when the database
# volume is first initialised. Table privileges are granted by the first migration.
set -eu

psql -v ON_ERROR_STOP=1 \
     --username "$POSTGRES_USER" \
     --dbname "$POSTGRES_DB" \
     -v app_password="$NOVAWALLET_APP_PASSWORD" <<'SQL'
CREATE ROLE novawallet_app LOGIN PASSWORD :'app_password';
GRANT CONNECT ON DATABASE novawallet TO novawallet_app;
SQL
