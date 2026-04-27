# Database migrations (Supabase / PostgreSQL)

Run SQL files **in order** (`001` → `008`) in the Supabase SQL Editor (or your CI migration step) against the project database.

| File | Purpose |
|------|---------|
| `001_initial_app_users.sql` | Root `app_users` table for Clerk IDs |
| `002_chat_notes.sql` | `chat_notes` + FK to `app_users` |
| `003_job_applications.sql` | `job_applications` + FK to `app_users` |
| `004_career_profiles.sql` | `career_profiles` + FK to `app_users` |
| `005_chat_sessions.sql` | `chat_sessions` + `chat_transcripts` + FK to `app_users` |
| `006_learning_memory.sql` | `learning_memories` + FK to `app_users` |
| `007_token_usage.sql` | Token usage aggregates (`token_usage_*`) + registered users |
| `008_user_usage_plan.sql` | `user_usage_daily` + `user_plan` (daily limits + plan rows) |
| `009_cv_pdf_exports.sql` | `cv_pdf_exports` (PDF download tracking + quota) |
| `010_cv_pdf_exports_target_fields.sql` | Adds `target_company` / `target_role` to `cv_pdf_exports` |
| `011_cv_resume_categories.sql` | `cv_user_categories` + `cv_resume_category_assignments` (server-side master CV categories, replaces localStorage) |

## Backfill chat notes from Redis (manual)

When `DatabaseFeatures:ChatNotesStorage` is switched to `postgres`, run a one-off backfill (script or admin tool) that:

1. For each user key pattern `chatnotes:v2:{userId}:index`, read note ids.
2. For each id, read `chatnotes:v2:{userId}:n:{noteId}` JSON.
3. `INSERT INTO app_users (clerk_user_id) VALUES ($userId) ON CONFLICT DO NOTHING`.
4. `INSERT INTO chat_notes (...) ON CONFLICT (id) DO UPDATE` from JSON fields.

Until backfill completes, new writes can go to Postgres while old data remains in Redis — prefer a maintenance window or dual-write (not implemented by default).

## Backfill job applications from Redis (manual / admin)

When `DatabaseFeatures:JobApplicationsStorage` is `postgres`:

1. Ensure `003_job_applications.sql` is applied.
2. Use `POST /api/admin/migrations/backfill-job-applications/{userId}` (admin-only) to copy `job_apps:{userId}` from Redis into Postgres for one user, **preserving timestamps** from the stored JSON.
3. Test on staging first; back up or export Redis if needed.

## Backfill career profile from Redis (manual / admin)

When `DatabaseFeatures:CareerProfileStorage` is `postgres`:

1. Ensure `004_career_profiles.sql` is applied.
2. Use `POST /api/admin/migrations/backfill-career-profile/{userId}` (admin-only) to copy `profile:{userId}`, `profile:{userId}:cv_raw`, and `profile_version:{userId}` into Postgres.
3. Test on staging first.

## Chat sessions: Postgres vs Redis

When `DatabaseFeatures:ChatSessionStorage` is `postgres`, the API stores the session list in `chat_sessions` and message arrays in `chat_transcripts` (instead of Upstash keys `chat_sessions_index:{userId}` and `chat_transcript:{userId}:{sessionId}`). This reduces Redis command volume for list/sync traffic.

1. Ensure `005_chat_sessions.sql` is applied and `app_users` contains the Clerk user id (same FK requirement as other tables).
2. Optional: `POST /api/admin/migrations/backfill-chat-sessions/{userId}` (admin-only) copies the Redis index + transcripts for one user into Postgres. Test on staging first.

## Learning memory: Postgres vs Redis

When `DatabaseFeatures:LearningMemoryStorage` is `postgres`, insights are stored in `learning_memories` (instead of the Upstash key `learning:{userId}`).

1. Ensure `006_learning_memory.sql` is applied.
2. Optional: `POST /api/admin/migrations/backfill-learning-memory/{userId}` (admin-only) copies Redis JSON into Postgres for one user. Test on staging first.

## Token usage + daily usage limits: Postgres vs Redis

1. Apply **`007_token_usage.sql`** then **`008_user_usage_plan.sql`** after earlier migrations (order matters).
2. Set `DatabaseFeatures:PostgresEnabled=true` and, when ready to cut over:
   - `DatabaseFeatures:TokenUsageStorage=postgres` — LLM token metrics (dashboard, admin) read/write in `token_usage_*` tables instead of Redis hashes.
   - `DatabaseFeatures:UsageStorage=postgres` — per-day request counts and `user_plan` in Postgres; Stripe customer/webhook keys remain in Redis.
3. **Backfill**: historical token data lives in Redis until migrated. Compare Redis vs Postgres metrics on staging before enabling production flags. A dedicated admin backfill endpoint may be added later (`Redis → Postgres` for a user/date range).

## Troubleshooting: empty tables but Redis works

If logs show **`Failed to connect`** to an **IPv6** address (`2a05:…`) and **`Network is unreachable`**, the API never reaches Postgres — **migrations and empty tables are not the cause**. Typical fix: hosting egress without IPv6 (e.g. Render) while Supabase DNS returns AAAA first. The API resolves **`*.supabase.co`** to an **IPv4** address when building the Npgsql connection string (see `SupabaseConnectionString.TryRewriteSupabaseHostToIpv4`). Alternatively use Supabase **Session pooler** (port **6543**) in your URI, or set **`DOTNET_SYSTEM_NET_DISABLEIPV6=1`** on the API host per [.NET networking docs](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/networking).
