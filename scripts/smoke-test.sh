#!/usr/bin/env bash
# End-to-end check against a running stack (`docker compose up`): the README walkthrough, with
# every status code asserted. Needs only bash, curl and sed.
#
#   ./scripts/smoke-test.sh                 # against http://localhost:8080
#   BASE_URL=http://host:8080 ./scripts/smoke-test.sh
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
RUN="$(date +%s)"
STATUS=""
BODY=""
HEADERS=""

fail() { echo "FAIL: $*" >&2; echo "$BODY" >&2; exit 1; }

# call METHOD PATH [TOKEN] [JSON] [IDEMPOTENCY_KEY]
call() {
  local method="$1" path="$2" token="${3:-}" json="${4:-}" key="${5:-}"
  local args=(-s -X "$method" "$BASE_URL$path" -D /tmp/novawallet-smoke-headers -o /tmp/novawallet-smoke-body -w '%{http_code}')
  [[ -n "$token" ]] && args+=(-H "Authorization: Bearer $token")
  [[ -n "$json" ]] && args+=(-H "Content-Type: application/json" -d "$json")
  [[ -n "$key" ]] && args+=(-H "Idempotency-Key: $key")
  STATUS="$(curl "${args[@]}")"
  BODY="$(cat /tmp/novawallet-smoke-body)"
  HEADERS="$(cat /tmp/novawallet-smoke-headers)"
}

expect() { [[ "$STATUS" == "$1" ]] || fail "$2: expected $1, got $STATUS"; echo "ok   $STATUS $2" >&2; }
field() { echo "$BODY" | sed -E "s/.*\"$1\":\"?([^\",}]*)\"?.*/\1/"; }

echo "Waiting for $BASE_URL/health/ready ..."
for _ in $(seq 1 90); do
  if [[ "$(curl -s -o /dev/null -w '%{http_code}' "$BASE_URL/health/ready" || true)" == "200" ]]; then break; fi
  sleep 2
done
call GET /health/ready; expect 200 "readiness"
call GET /health/live; expect 200 "liveness"
call GET /swagger/v1/swagger.json; expect 200 "OpenAPI document"

token() {
  call POST /dev/token "" "{\"subject\":\"$1\",\"scopes\":[\"$2\"]}"
  expect 200 "token for $1 ($2)"
  field accessToken
}
ALICE="alice-$RUN"; BOB="bob-$RUN"
ALICE_TOKEN="$(token "$ALICE" wallet)"
BOB_TOKEN="$(token "$BOB" wallet)"
NIP_TOKEN="$(token nip-gateway ledger:credit)"
AUDITOR_TOKEN="$(token auditor-1 audit:read)"

call POST /api/v1/wallets "$ALICE_TOKEN" "{\"customerId\":\"$ALICE\"}"; expect 201 "create Alice's wallet"
ALICE_WALLET="$(field walletId)"
call POST /api/v1/wallets "$BOB_TOKEN" "{\"customerId\":\"$BOB\"}"; expect 201 "create Bob's wallet"
BOB_WALLET="$(field walletId)"
call POST /api/v1/wallets "$ALICE_TOKEN" "{\"customerId\":\"$ALICE\"}"; expect 409 "second wallet refused"

call POST "/api/v1/wallets/$ALICE_WALLET/credits" "$NIP_TOKEN" '{"amountKobo":5000000}' "nip-session-$RUN"
expect 201 "credit Alice ₦50,000"
call POST "/api/v1/wallets/$ALICE_WALLET/credits" "$ALICE_TOKEN" '{"amountKobo":5000000}' "self-credit-$RUN"
expect 403 "customer can't credit"

call GET "/api/v1/wallets/$ALICE_WALLET/balance" "$ALICE_TOKEN"; expect 200 "Alice's balance"
[[ "$(field balanceKobo)" == "5000000" ]] || fail "balance should be 5000000"
call GET "/api/v1/wallets/$ALICE_WALLET/balance" "$BOB_TOKEN"; expect 404 "Bob can't see Alice's wallet"

TRANSFER="{\"sourceWalletId\":\"$ALICE_WALLET\",\"destinationWalletId\":\"$BOB_WALLET\",\"amountKobo\":1250000}"
call POST /api/v1/transfers "$ALICE_TOKEN" "$TRANSFER" "pay-bob-$RUN"; expect 201 "transfer ₦12,500 to Bob"
[[ "$(field sourceBalanceAfterKobo)" == "3750000" ]] || fail "Alice should have 3750000 left"
call POST /api/v1/transfers "$ALICE_TOKEN" "$TRANSFER" "pay-bob-$RUN"; expect 201 "retry with the same key"
grep -qi '^Idempotent-Replayed: true' <<<"$HEADERS" || fail "retry should be a replay"
call POST /api/v1/transfers "$ALICE_TOKEN" "${TRANSFER/1250000/7}" "pay-bob-$RUN"; expect 422 "same key, different body"
call POST /api/v1/transfers "$ALICE_TOKEN" "${TRANSFER/1250000/999999999}" "too-much-$RUN"; expect 422 "insufficient funds"
[[ "$(field code)" == "INSUFFICIENT_FUNDS" ]] || fail "code should be INSUFFICIENT_FUNDS"
call POST /api/v1/transfers "$ALICE_TOKEN" "$TRANSFER"; expect 400 "missing Idempotency-Key"

call GET "/api/v1/wallets/$ALICE_WALLET/statement?limit=10" "$ALICE_TOKEN"; expect 200 "Alice's statement"
grep -q '"type":"TRANSFER_OUT"' <<<"$BODY" || fail "statement should show the transfer"
call GET "/api/v1/wallets/$BOB_WALLET/balance" "$BOB_TOKEN"; expect 200 "Bob's balance"
[[ "$(field balanceKobo)" == "1250000" ]] || fail "Bob should have 1250000"

call GET "/api/v1/audit?walletId=$ALICE_WALLET" "$AUDITOR_TOKEN"; expect 200 "audit trail for Alice"
grep -q '"action":"TRANSFER_DEBIT"' <<<"$BODY" || fail "audit should show the debit"
call GET "/api/v1/audit" "$ALICE_TOKEN"; expect 403 "customer can't read the audit trail"
call GET "/api/v1/wallets/$ALICE_WALLET/balance"; expect 401 "no token"

echo "All checks passed."
