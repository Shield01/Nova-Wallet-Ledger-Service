# AI usage

I built this with Claude doing most of the typing, and with a process that keeps it honest. Claude
never decided scope on its own, and nothing merged without passing format, build and tests on real
PostgreSQL and an independent review. This file covers the tools, three of the prompts that shaped
the work, and the fourteen times the AI got something wrong for a financial system, with how each
was caught and fixed.

## Tools

| Tool | What it did |
|---|---|
| **Claude Opus 5.5** (Anthropic), working as an agent in a cloud Linux workspace | Turned the brief into scope and stories, wrote the design contract ([docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)), wrote all the code and tests, ran `dotnet format`, builds and tests, ran Docker, and handled git and GitHub (issues, pull requests, merges) |
| **Independent Claude agents** with a fresh context, read-only | A readiness reviewer that checked every story before any code (two rounds), and three review lanes (security, code quality, this project's standards) that reviewed the code before each batch of pull requests |
| **GitHub Issues and pull requests** | The tracker and the record: one issue per story with its acceptance criteria, one pull request per story with its verification |

## How the work was directed

I use a structured way of running AI on delivery work, my "code factory". In short:

1. **Brief → scope.** Actors, epics, the brief's rules, 18 assumptions and one open question. I
   reviewed it and answered the question (make the repository public) before any stories were
   written.
2. **Scope → stories.** Twenty stories, each with acceptance criteria traced to the brief. An
   independent agent then reviewed every story against a definition of ready, adversarially and
   with money in mind. Two rounds of findings were fixed or decided, and each decision was written
   down.
3. **Stories → issues** in GitHub, with dependencies, in a fixed build order: hard constraints
   first, stretch goals last.
4. **Each story** was built in small units behind gates that are never skipped: `dotnet format`,
   a Release build with warnings as errors, and the full test suite against real PostgreSQL. Then
   three independent review lanes read the diff, each through one lens, and every finding was
   fixed or answered in the same batch of pull requests. Each pull request lists its acceptance
   criteria and the gate results on that exact commit.
5. **Human checkpoints.** I approved the scope and set the ground rules (public repository, my
   name on the commits, the harness kept private). Then I delegated the remaining approvals and
   the merges while I was away, and every decision made under that delegation was logged with its
   time and reason for me to review.
6. **Every AI mistake was logged when it was caught.** The list below comes from that log.

## Three prompts

**1. The kick-off** (me, 17 Oct, with the brief attached):

> "I think we have a perfect candidate for us to test out our factory… I just noticed a mail they
> sent yesterday, which contains a take home assignment."

*What came back:* a scope document (7 epics, 19 features, 18 assumptions, 1 open question about
repository access), presented for approval rather than acted on.
*What I did:* approved it ("Your understanding of the scope is alright, you can proceed to write
the story") and answered the open question.

**2. Handing over the run** (me, 17 Oct):

> "You can also create the harness for this, following the playbook… let's just make the repo
> public… I expect that the job should be able to run almost without my oversight"

*What came back:* an access check before starting. Push access, NuGet and Docker Hub were blocked
in the sandbox, so it asked me four questions (who approves checkpoints, whether the harness goes in
the public repo, whether to draft the deck, how commits are authored) instead of guessing.
*What I did:* granted access, delegated approvals and merges, kept the harness private, and had
commits authored as me with Claude as co-author.

**3. The adversarial story review** (orchestrator to an independent agent, summarised):

> Check all 20 drafted stories against the 13-point definition of ready, adversarially, and look
> for financial-system risks.

*What came back:* 4 stories ready, 12 fixable, 4 needing a decision, and four real risks: a
one-second gap at midnight where transfers escaped the daily limit, a settlement-account hot row, a
superuser able to switch off the audit-log triggers, and a dev token issuer that could mint a
customer token able to credit money.
*What I did:* every finding was fixed or decided before a line of code was written (cases 1–3
below).

The same pattern then ran on the code: three review agents, one lens each, reading the diff at a
pinned commit and reporting each finding with a concrete failure scenario. That found cases 6–8 and
11–14.

## Where the AI was wrong

Five worth telling in full:

**A refusal could commit money that had already moved (case 6).** The AI-written idempotency
guard committed whatever the operation had written before it returned a refusal. For a refusal
outside the stored set, the transaction would commit money writes with no idempotency record, so a
retry with the same key would post the transfer again: a double spend through the very mechanism
meant to prevent one. The quality review lane caught it. The guard now takes a savepoint before
the operation and rolls back to it on any refusal, and tests prove that a write-then-refuse leaves
nothing behind and that the outcome is stored in the same transaction as the writes
([#40](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/40)).

**A customer could have minted a token that credits money (cases 3 and 7).** The first draft of
the dev issuer would put `wallet` and `ledger:credit` in one token. After that was fixed at story
review, the security lane found that the AI's validation skipped its checks after a null array
element, so `[null, "wallet ledger:credit"]` still produced a customer token that could credit any
wallet. Every element is now validated, and both cases have tests
([#31](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/31),
[#40](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/40)).

**The audit trail's immutability could be switched off (case 2).** The AI's design made the
database owner the Postgres superuser, which can `SET session_replication_role = replica` and
silently skip the triggers that make `audit_log` append-only. The owner is now a non-superuser,
with a test that tries exactly that and expects a refusal
([#31](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/31)).

**One second a night outside the daily limit (case 1).** The AI-drafted story defined the WAT day
as 00:00:00 to 23:59:59, a closed interval. Any transfer in the last second of the day belonged to
no day and escaped the limit. The day is now half-open, `[00:00, next 00:00)`, with boundary tests
([#39](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/39)).

**A token without a subject got unlimited transfers (case 12).** The AI-written rate limiter gave
a signed token with no `sub` an unlimited bucket, under a comment claiming that branch couldn't be
reached. Nothing enforced that claim. The scope policies now require `sub` (403 otherwise), the
limiter's fallback is a shared limited bucket, and a test covers it
([#46](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/46)).

All fourteen:

| # | What the AI produced | How it was caught | Fix |
|---|---|---|---|
| 1 | Daily-limit day as a closed interval, leaving about a second each night outside any day | Story readiness review | Half-open day with boundary tests ([#39](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/39)) |
| 2 | Database owner as superuser, able to switch off the audit triggers | Story readiness review | Non-superuser owner; tested ([#31](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/31)) |
| 3 | Dev issuer would mint `wallet` and `ledger:credit` in one token | Story readiness review | Combination refused; tested ([#31](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/31)) |
| 4 | First migration granted the runtime role UPDATE and DELETE on every future table, ledger included | Story readiness review | Per-table grants; ledger and audit are SELECT/INSERT only ([#31](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/31)) |
| 5 | Startup guard compared the key against a literal copy of the dev key: a second committed secret | Story readiness review (round 2) | Compare a SHA-256 fingerprint instead ([#34](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/34)) |
| 6 | Idempotency guard committed writes made before a refusal; a retry could post twice | Quality review lane | Savepoint rollback on every refusal; tests ([#40](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/40)) |
| 7 | Dev token validation skipped checks after a null element, minting a customer token that can credit | Security review lane | Every element validated; test ([#40](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/40)) |
| 8 | Responses returned 100 ns timestamps while PostgreSQL stores microseconds, so `createdAt` wouldn't match the ledger; hidden by a whole-second test clock | Quality review lane | "Now" truncated to microseconds once per use case; sub-microsecond test ([#40](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/40)) |
| 9 | Day boundaries as `+01:00` timestamps, which the Npgsql driver rejects, so every transfer failed once the limit check ran | Integration tests on real PostgreSQL (16 failures before commit) | Boundaries in UTC; unit test ([#39](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/39)) |
| 10 | Outbox test read `published_at` before the poll's transaction committed, failing about one run in five | Repeating the tests before commit | Test waits for the committed row ([#47](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/47)) |
| 11 | Swagger UI persisted pasted bearer tokens in localStorage | Security review lane | Persistence removed ([#43](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/43)) |
| 12 | Rate limiter gave a token without `sub` an unlimited bucket | Security and quality review lanes | Policies require `sub`; limited fallback; test ([#46](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/46)) |
| 13 | A publisher timeout rolled back the whole outbox batch every poll, republishing earlier messages and never counting the attempt | Quality review lane | Only shutdown propagates; timeouts are failed attempts; test ([#47](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/47)) |
| 14 | Load test counted rows across a database shared with another test, and checked a copy of the default limit | Quality and standards review lanes | Counts scoped to the run; limit read from configuration ([#44](https://github.com/Shield01/Nova-Wallet-Ledger-Service/pull/44)) |

## What this taught me about directing AI here

- **The AI is most wrong at the edges**: interval boundaries, a null inside an array, a
  cancellation exception, a privilege nobody thought about. The happy path was almost always right.
- **It writes comments that promise things the code doesn't enforce** (case 12). A reviewer that
  reads the code, not the comments, catches that.
- **One lens per reviewer finds more.** The security, quality and standards lanes each found things
  the others didn't, and a fresh context doesn't share the author's blind spots.
- **Real infrastructure in tests matters.** Case 9 only exists with the real PostgreSQL driver; an
  in-memory database would have passed it straight to production.
- **Run new concurrency tests more than once** (case 10).
- **Reviewers are wrong sometimes too.** One review finding read a stale branch; it was checked
  against the repository and dropped with a reason rather than "fixed".
