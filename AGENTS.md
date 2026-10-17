# NovaWallet Ledger Service

A wallet ledger for the fictional FirstBank NovaPay super-app: wallets, credits, transfers,
statements and an audit trail, in kobo, safe under concurrent requests.

## Commands

| Purpose | Command | Notes |
|---|---|---|
| Restore | `dotnet restore NovaWallet.sln` | |
| Format check | `dotnet format NovaWallet.sln --verify-no-changes` | |
| Build | `dotnet build NovaWallet.sln -c Release --no-restore` | |
| Tests | `dotnet test NovaWallet.sln -c Release --no-build` | Integration tests need Docker, or `NOVAWALLET_TEST_DB` pointing at a PostgreSQL server |
| Run everything | `docker compose up --build` | API on http://localhost:8080, Swagger at `/swagger` |

## Architecture in brief

Full design contract: `docs/ARCHITECTURE.md` (API, errors, money and concurrency rules, roles).

- Four projects: `Domain` ← `Application` ← `Infrastructure` ← `Api`. Application never references
  EF Core or ASP.NET.
- One use case = one application service method = one database transaction. Money moves only
  inside that transaction, with wallet rows locked `FOR UPDATE` in ascending id order.
- Amounts are `long` kobo everywhere. No `float`, `double` or `decimal` in the money path.
- Every movement is double-entry; credits come from the `NIP_INBOUND_SETTLEMENT` system account.
- Errors are RFC 7807 Problem Details with a `code` from a fixed catalogue.

## Conventions

- Branch from `main` as `feature/<issue>-<slug>`; one story per PR; commit subjects end `(#<issue>)`.
- Configuration over constants: every tunable has a key and a default in `appsettings.json`.
- Time comes from the injected `TimeProvider`, never `DateTime.UtcNow`.
- Test names describe behaviour; every acceptance criterion has a test.

## Do not

- Do not use `float`, `double` or `decimal` for amounts.
- Do not read a balance and write it back without holding the row lock in the same transaction.
- Do not log request bodies, tokens, signing keys or connection strings.
- Do not return 403 for a wallet the caller doesn't own; return 404 `WALLET_NOT_FOUND`.
- Do not migrate the database on normal API startup; the `migrator` service does that.

## Where things live

| What | Path |
|---|---|
| Entities and domain errors | `src/NovaWallet.Domain` |
| Use cases and rules | `src/NovaWallet.Application` |
| Persistence, migrations, outbox | `src/NovaWallet.Infrastructure` |
| Controllers, auth, middleware | `src/NovaWallet.Api` |
| Unit and architecture tests | `tests/NovaWallet.UnitTests` |
| HTTP tests on real PostgreSQL | `tests/NovaWallet.IntegrationTests` |
| Compose and database init | `docker-compose.yml`, `deploy/` |
