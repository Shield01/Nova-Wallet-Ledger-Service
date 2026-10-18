# Architecture — NovaWallet Ledger Service

This is the design contract the code follows. API shapes, error codes, database rules and
concurrency rules live here; a change to any of them is made in the same pull request as the
code that needs it.

## System shape

A single ASP.NET Core 8 service over PostgreSQL 16. HTTP requests pass through middleware
(correlation ID → exception handling → status-code problems → OpenAPI document and UI →
authentication → authorisation → rate limiting) into controllers. Controllers validate input, then call one application service per
use case. The service runs the whole use case inside **one database transaction**: lock the wallet
rows, read the clock, check the rules, post ledger entries, write audit records, write the outbox
message, store the idempotency record, commit. Nothing touching money happens outside that
transaction.

```
HTTP → Api (controllers, middleware, auth, problem details)
     → Application (use cases, rules, ports)
     → Infrastructure (EF Core + Npgsql, row locks, audit, idempotency, outbox)
     → PostgreSQL
```

## Solution layout

| Project | Path | Owns | May depend on | Must not depend on |
|---|---|---|---|---|
| Domain | `src/NovaWallet.Domain` | Entities, value rules, error codes, `Result<T>` | nothing | anything else |
| Application | `src/NovaWallet.Application` | Use-case services, business rules (balance, limit, idempotency semantics), ports for persistence, time, caller and correlation | Domain | Infrastructure, Api, EF Core, ASP.NET |
| Infrastructure | `src/NovaWallet.Infrastructure` | `LedgerDbContext`, EF configurations, migrations, port implementations, outbox dispatcher | Application, Domain | Api |
| Api | `src/NovaWallet.Api` | `Program.cs` composition root, controllers, DTOs, auth, problem details, middleware, OpenAPI | all of the above | — |
| UnitTests | `tests/NovaWallet.UnitTests` | Domain and Application tests, architecture tests | Domain, Application | — |
| IntegrationTests | `tests/NovaWallet.IntegrationTests` | HTTP-level tests via `WebApplicationFactory<Program>` against real PostgreSQL | all | — |

Solution file: `NovaWallet.sln`. Shared build settings in `Directory.Build.props` (nullable on,
warnings as errors). Package versions pinned centrally in `Directory.Packages.props`.

## Money rules

1. Amounts are `long` kobo end to end: DTOs, domain, database (`bigint`). `float`, `double` and
   `decimal` never appear in the money path; an architecture test enforces this by reflection.
2. Money arithmetic is `checked`, so an overflow throws instead of wrapping silently.
3. Every movement is double-entry: a ledger transaction has two entries whose signed amounts sum to
   zero. Entry types: `CREDIT` (customer side of a credit), `CREDIT_SETTLEMENT` (system side of a
   credit), `TRANSFER_OUT`, `TRANSFER_IN`. `ledger_transactions.id` is a UUID and is the
   `transactionId` / `transferId` everywhere (responses, statement, audit, outbox).
   `ledger_entries.id` is a `bigserial`, so ids grow with insertion order and break timestamp ties.
4. The system account `NIP_INBOUND_SETTLEMENT` has the fixed id
   `00000000-0000-0000-0000-000000000001`, no customer, and is seeded by migration. Credits debit
   it, so its balance is negative. Customers can never read it or transfer from or to it: it
   behaves as not found. Auditors may filter the audit trail by it.
5. The wallet row's `balance_kobo` is the running balance, updated in the same transaction as the
   entries. A database `CHECK` keeps customer wallets at or above zero as a last line of defence.
   Each entry stores `balance_after_kobo`.
6. Wallet ids are random (version 4) UUIDs, so they can't be guessed or enumerated.

## Concurrency

- Every money use case opens a transaction (`READ COMMITTED`) and locks the wallet rows it will
  touch with `SELECT … FOR UPDATE`, **in ascending wallet id order**, before reading balances.
  Ordered locking prevents deadlocks between opposite-direction transfers.
- Lock waits are bounded by `Database:LockTimeoutMilliseconds` (default 5000) through PostgreSQL
  `lock_timeout`. A timed-out wait rolls back and returns 503 `SERVICE_UNAVAILABLE`.
- "Now" is read **once**, after the locks are held, and used for the daily-limit window and every
  timestamp the use case writes.
- The daily-limit check runs after the source wallet lock is held, in the same transaction, so
  concurrent transfers from one wallet are serialised for both the balance and the limit.
- Known trade-off: every credit locks the system account row, so credits are serialised system-wide.
  That is acceptable at this scale; production would derive the settlement balance from entries or
  shard settlement accounts (README).

## Idempotency

Applies to `POST /api/v1/transfers` and `POST /api/v1/wallets/{id}/credits`.

- Header `Idempotency-Key`: required. Missing or empty → 400 `IDEMPOTENCY_KEY_REQUIRED`. Longer
  than 128 characters, or containing a character outside printable ASCII (0x21–0x7E) → 400
  `IDEMPOTENCY_KEY_INVALID`.
- Scope: the caller (`sub`) plus the key. Table `idempotency_records`, primary key
  `(caller_id, idempotency_key)`, columns `request_hash`, `outcome` (`SUCCESS` or an error code),
  `response_body` (text: the use-case result as JSON, or the refusal's detail and extensions as
  JSON; the Api maps either to the HTTP response), `created_at`.
- `request_hash` is SHA-256 over the operation name and the parsed request values in a fixed order,
  so JSON property order doesn't matter. The same key reused on another operation or payload is a
  mismatch.
- Inside the use-case transaction, first take `pg_try_advisory_xact_lock` on a 64-bit hash of
  `caller_id:key`. Not acquired → 409 `IDEMPOTENCY_REQUEST_IN_PROGRESS`. Then look up the record:
  same hash → replay; different hash → 422 `IDEMPOTENCY_KEY_REUSED`. No record → run the use case,
  then insert the record **in the same transaction** as its writes.
- The Application layer stores the outcome in layer-neutral form; the Api renders it the same way
  on the first response and on every replay. A replay carries response header
  `Idempotent-Replayed: true` and re-stamps `traceId`, `correlationId` and `instance` for the
  current request; everything else is identical.
- Stored outcomes: success, and the refusals `INSUFFICIENT_FUNDS`, `DAILY_LIMIT_EXCEEDED`,
  `SAME_WALLET_TRANSFER`, `DESTINATION_WALLET_NOT_FOUND`, `WALLET_NOT_FOUND`. Not stored: validation
  (400), auth (401/403), rate limiting (429), 503 and unexpected errors (500); these roll the
  transaction back.
- Client contract (README): after a timeout, a 5xx or a 409, retry with the **same** key; use a new
  key only for a new payment.
- Records don't expire in this build; a TTL is a production follow-up (README).

### Transfer refusal precedence

When several refusals apply, the first in this order wins (and is what gets stored):
400 `VALIDATION_FAILED` → 400 `IDEMPOTENCY_KEY_REQUIRED` / `IDEMPOTENCY_KEY_INVALID` → idempotency
(replay / 409 / 422 reuse) → 404 `WALLET_NOT_FOUND` (source
unknown or not owned) → 422 `SAME_WALLET_TRANSFER` → 422 `DESTINATION_WALLET_NOT_FOUND` → 422
`INSUFFICIENT_FUNDS` → 422 `DAILY_LIMIT_EXCEEDED`.

## Daily outbound limit

- Default 50,000,000 kobo (₦500,000), from configuration `Limits:DailyOutboundKobo`.
- The day is the WAT calendar day, a fixed UTC+01:00 offset (no daylight saving): the half-open
  interval [00:00 WAT, next 00:00 WAT). Computed from the injected `TimeProvider`, never
  `DateTime.Now` / `DateTime.UtcNow`.
- Counts the source wallet's `TRANSFER_OUT` entries with `created_at` in the current day.
- A transfer that would take the day's total above the limit is refused in full with 422
  `DAILY_LIMIT_EXCEEDED`, extensions `dailyLimitKobo` and `remainingKobo`. Reaching it exactly is
  allowed. Credits, incoming transfers and refusals don't count.

## Audit trail

- Table `audit_log`, separate from the ledger: `id` (bigserial), `occurred_at`, `action` (`CREDIT`,
  `SYSTEM_DEBIT`, `TRANSFER_DEBIT`, `TRANSFER_CREDIT`), `wallet_id` (uuid, references `wallets`),
  `transaction_id` (uuid; its foreign key to `ledger_transactions` is added by the migration that
  creates that table), `amount_kobo` (signed delta), `balance_before_kobo`, `balance_after_kobo`,
  `actor` (token `sub`), `correlation_id`.
- One record per balance mutation, including the system account side, written in the same
  transaction as the mutation through the audit port.
- Immutable at the database level: triggers reject `UPDATE`, `DELETE` and `TRUNCATE` on
  `audit_log`. The runtime role has only `SELECT` and `INSERT` on it, and the schema owner is not a
  superuser, so it can't switch triggers off with `session_replication_role`. The owner could still
  `DROP TRIGGER` or `ALTER TABLE … DISABLE TRIGGER` in a migration; separating the migration role
  from the table owner and adding tamper evidence (a hash chain) are production follow-ups.
- Read through `GET /api/v1/audit` (scope `audit:read`) and directly with SQL (README).

## Outbox

- `TransferCompleted` is inserted into `outbox_messages` (`id` (uuid, the event id), `type`,
  `payload` jsonb, `occurred_at`, `published_at`, `attempts`, `last_error`) in the transfer's
  transaction. Payload: `{ eventId, transferId, sourceWalletId, destinationWalletId, amountKobo,
  occurredAt, correlationId }`.
- A `BackgroundService` polls every `Outbox:PollIntervalMilliseconds` (default 1000), claims
  unpublished rows with `FOR UPDATE SKIP LOCKED`, publishes through an `IEventPublisher` port, and
  sets `published_at`. The default publisher writes one structured log line.
- Delivery is at least once; consumers de-duplicate on `eventId`. A failed publish increments
  `attempts` and records `last_error`; after `Outbox:MaxAttempts` (default 10) the dispatcher stops
  retrying that message and logs an error.

## API contract

Base path `/api/v1`. JSON, camelCase. Every amount field ends in `Kobo` and is a JSON integer.
Numbers written as strings are rejected.

| Method and path | Auth | Request | Success |
|---|---|---|---|
| `POST /api/v1/wallets` | scope `wallet` | `{ "customerId": string }` (must equal `sub`) | 201 `Wallet`, `Location: /api/v1/wallets/{walletId}/balance` |
| `GET /api/v1/wallets/{walletId}/balance` | scope `wallet`, owner | — | 200 `{ walletId, balanceKobo, currency: "NGN", asOf }` |
| `POST /api/v1/wallets/{walletId}/credits` | scope `ledger:credit`; `Idempotency-Key` | `{ "amountKobo": long }` | 201 `{ transactionId, walletId, type: "CREDIT", amountKobo, balanceAfterKobo, createdAt }` |
| `POST /api/v1/transfers` | scope `wallet`, owner of source; `Idempotency-Key` | `{ sourceWalletId, destinationWalletId, amountKobo }` | 201 `{ transferId, sourceWalletId, destinationWalletId, amountKobo, sourceBalanceAfterKobo, createdAt }` |
| `GET /api/v1/wallets/{walletId}/statement?limit=&cursor=` | scope `wallet`, owner | — | 200 `{ walletId, items: [StatementEntry], nextCursor }` |
| `GET /api/v1/audit?walletId=&limit=&cursor=` | scope `audit:read` | — | 200 `{ items: [AuditEntry], nextCursor }` |
| `POST /dev/token` | anonymous; Development only | `{ subject, scopes: [string] }` | 200 `{ accessToken, tokenType: "Bearer", expiresAt }` |
| `GET /health/live`, `GET /health/ready` | anonymous | — | 200 / 503 |
| `GET /swagger`, `GET /swagger/v1/swagger.json` | anonymous; only when `OpenApi:Enabled` (otherwise unmapped: see `NOT_FOUND` under Errors) | — | 200 |

- `Wallet`: `{ walletId, customerId, currency: "NGN", balanceKobo, createdAt }`.
- `StatementEntry`: `{ entryId, transactionId, type: CREDIT|TRANSFER_IN|TRANSFER_OUT, direction:
  CREDIT|DEBIT, amountKobo (positive), balanceAfterKobo, counterpartyWalletId (null for CREDIT),
  createdAt }`.
- `AuditEntry`: the `audit_log` columns in camelCase.
- Pagination: newest first, ties broken by id descending; `limit` default 20, 1–100; `cursor` is
  opaque (base64url of the last item's timestamp and id: `createdAt` for statements, `occurredAt`
  for audit); `nextCursor` is null on the last page. Entries that existed when paging began appear
  exactly once; entries added later never cause a repeat or a skip.
- Audit `walletId` filter: malformed → 400 `VALIDATION_FAILED`; unknown → 404 `WALLET_NOT_FOUND`;
  the system account is allowed.
- A wallet that doesn't exist, or that the caller doesn't own, is 404 `WALLET_NOT_FOUND` (never
  403). A transfer to a non-existent destination does reveal that it doesn't exist (422); the payer
  has to be told.
- `amountKobo`: integer, 1 to 9,007,199,254,740,991 (2^53 − 1, the largest integer JSON clients
  carry exactly). `customerId` and token `subject`: 1–64 characters of `[A-Za-z0-9_-]`.

## Errors (RFC 7807 Problem Details)

Every non-2xx response is `application/problem+json` with `type`
(`https://novawallet.example/errors/{kebab-code}`), `title` (fixed per code), `status`, `detail`,
`instance`, and extensions `code`, `traceId` (the W3C trace id, the same value as `TraceId` in the
logs), `correlationId`. Validation errors add `errors`
(camelCase field → messages). No stack traces or internal messages leave the service.

| Status | `code` | When |
|---|---|---|
| 400 | `VALIDATION_FAILED` | Body or query fails validation |
| 400 | `IDEMPOTENCY_KEY_REQUIRED` / `IDEMPOTENCY_KEY_INVALID` | See Idempotency |
| 401 | `UNAUTHENTICATED` | Missing, malformed, expired or wrongly signed token |
| 403 | `FORBIDDEN` | Token lacks the required scope, or `customerId` ≠ `sub` on wallet creation |
| 404 | `WALLET_NOT_FOUND` | Unknown or not-owned wallet |
| 404 | `NOT_FOUND` | Unknown route, or a route value that isn't a valid id (authenticated callers; anonymous callers get 401 for any route not marked anonymous) |
| 405 | `METHOD_NOT_ALLOWED` | Known route, unsupported method |
| 409 | `WALLET_ALREADY_EXISTS` | Customer already has a wallet; extension `existingWalletId` |
| 409 | `IDEMPOTENCY_REQUEST_IN_PROGRESS` | Same key still being processed |
| 415 | `UNSUPPORTED_MEDIA_TYPE` | Body isn't JSON |
| 422 | `IDEMPOTENCY_KEY_REUSED` | Same key, different payload |
| 422 | `INSUFFICIENT_FUNDS` | Source balance below the amount |
| 422 | `DAILY_LIMIT_EXCEEDED` | See Daily outbound limit |
| 422 | `SAME_WALLET_TRANSFER` | Source equals destination |
| 422 | `DESTINATION_WALLET_NOT_FOUND` | Destination doesn't exist or is the system account |
| 429 | `RATE_LIMITED` | Transfer rate limit hit; `Retry-After` header |
| 500 | `INTERNAL_ERROR` | Anything unexpected |
| 503 | `SERVICE_UNAVAILABLE` | Database unreachable, lock wait timed out, or not ready |

## Authentication and authorisation

- JWT bearer, HS256 only. Settings under `Auth`: `Issuer` (default `novawallet-dev-issuer`),
  `Audience` (default `novawallet-api`), `SigningKey` (at least 32 UTF-8 bytes, environment only),
  `TokenLifetimeMinutes` (default 15). Clock skew 30 seconds. Inbound claim mapping is off, so
  `sub` and `scope` keep their names. `scope` is space-delimited.
- Scopes: `wallet` (customer endpoints), `ledger:credit` (inbound-transfer system), `audit:read`
  (auditors). One named policy per scope. A fallback policy requires an authenticated caller on
  every endpoint not explicitly marked anonymous.
- Ownership: the wallet's `customer_id` must equal `sub`; otherwise 404 `WALLET_NOT_FOUND`.
- Dev issuer: `POST /dev/token` exists only when `Auth:DevIssuer:Enabled` is true **and** the
  environment is `Development`. It refuses to put `wallet` and `ledger:credit` in the same token, so
  a customer token can never credit. Startup fails if the dev issuer is enabled outside
  Development, if the signing key is missing or shorter than 32 bytes, or if the environment is
  outside Development and the key's SHA-256 fingerprint matches the committed development default
  (the code holds only the fingerprint, never a second copy of the key). Token validation failures
  return a bare `WWW-Authenticate: Bearer` challenge, never the reason. The `--migrate` mode configures no
  authentication and runs none of these guards.
- Clocks: token minting and lifetime validation both use the wall clock, because the JWT library
  validates against it. Business time (ledger timestamps, the daily window) uses the injected
  `TimeProvider`.
- With HS256, whoever can verify tokens can mint them; production would use an asymmetric key from
  a real issuer (README).

## Configuration

Tunables come from configuration with defaults in `appsettings.json`, overridden by environment
variables (`Section__Key`). Secrets (signing key, database passwords) come only from the
environment. Keys: `ConnectionStrings:Ledger`, `ConnectionStrings:LedgerMigrations`, `Auth:*`,
`Limits:DailyOutboundKobo`, `Database:LockTimeoutMilliseconds` (5000),
`RateLimiting:Transfers:PermitLimit` (10), `RateLimiting:Transfers:WindowSeconds` (60),
`Outbox:PollIntervalMilliseconds` (1000), `Outbox:MaxAttempts` (10), `OpenApi:Enabled` (true
except in Production).

## Database and startup

- PostgreSQL 16. Tables in `snake_case`: `wallets`, `ledger_transactions`, `ledger_entries`,
  `audit_log`, `idempotency_records`, `outbox_messages`.
- Roles. `postgres` (superuser) is used only by the init script. `novawallet_owner` (not a
  superuser) owns the database and schema and runs migrations. `novawallet_app` is the runtime role.
  Each migration grants the runtime role exactly what it needs on the tables it creates:

  | Table | `novawallet_app` privileges |
  |---|---|
  | `wallets` | SELECT, INSERT, UPDATE of `balance_kobo` only |
  | `ledger_transactions`, `ledger_entries` | SELECT, INSERT (the ledger is append-only) |
  | `audit_log` | SELECT, INSERT |
  | `idempotency_records` | SELECT, INSERT |
  | `outbox_messages` | SELECT, INSERT, UPDATE |
  | sequences | USAGE, SELECT |

- Migrations are EF Core migrations in Infrastructure. A one-shot `migrator` compose service runs
  the API image with `--migrate` as `novawallet_owner`, then exits. The `api` service starts after
  it completes and connects as `novawallet_app`. The API never migrates on normal startup.
- `docker compose up` starts `db` (with a `pg_isready` healthcheck and an init script creating both
  roles), `migrator`, and `api` on port 8080.

## Observability

- JSON console logging with scopes. Middleware takes `X-Correlation-ID` from the request (1–64
  characters of `[A-Za-z0-9-]`) or generates one, echoes it on the response, and adds
  `CorrelationId` and `TraceId` to the logging scope.
- Never log request bodies, tokens, signing keys or connection strings.
- The transfer rate limiter is in memory per instance; several instances would each allow the
  limit (README).

## Patterns to follow

| Situation | Follow |
|---|---|
| New endpoint | `WalletsController` (wallet creation) |
| New money use case | the credit use case |
| New migration with grants | the `wallets` migration |
| New integration test | `tests/NovaWallet.IntegrationTests/Infrastructure/NovaWalletApiFactory.cs` |

## Testing

- Integration tests run against real PostgreSQL. By default the fixture starts a
  `postgres:16-alpine` container (Testcontainers). If `NOVAWALLET_TEST_DB` holds a connection string
  for a login that can create databases and roles, the fixture uses that server instead.
- Once per run the fixture creates `novawallet_owner` and `novawallet_app` exactly as the init
  script does. Each test class gets its own database owned by `novawallet_owner`, migrated as that
  role; the app under test connects as `novawallet_app`. The host runs in the `Development`
  environment with a signing key generated per run.
- Tests replace `TimeProvider` with a controllable fake for day-boundary cases and raise the rate
  limit unless the test is about rate limiting.
- After any test that moves money, assert the ledger invariants: entries per transaction sum to
  zero, and every customer wallet's `balance_kobo` equals the sum of its entries.
