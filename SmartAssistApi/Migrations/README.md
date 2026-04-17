# Database migrations (Supabase / PostgreSQL)

Run SQL files **in order** (`001` → `002` → `003` → `004`) in the Supabase SQL Editor (or your CI migration step) against the project database.

| File | Purpose |
|------|---------|
| `001_initial_app_users.sql` | Root `app_users` table for Clerk IDs |
| `002_chat_notes.sql` | `chat_notes` + FK to `app_users` |
| `003_job_applications.sql` | `job_applications` + FK to `app_users` |
| `004_career_profiles.sql` | `career_profiles` + FK to `app_users` |

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

## Troubleshooting: empty tables but Redis works

If logs show **`Failed to connect`** to an **IPv6** address (`2a05:…`) and **`Network is unreachable`**, the API never reaches Postgres — **migrations and empty tables are not the cause**. Typical fix: hosting egress without IPv6 (e.g. Render) while Supabase DNS returns AAAA first. The API resolves **`*.supabase.co`** to an **IPv4** address when building the Npgsql connection string (see `SupabaseConnectionString.TryRewriteSupabaseHostToIpv4`). Alternatively use Supabase **Session pooler** (port **6543**) in your URI, or set **`DOTNET_SYSTEM_NET_DISABLEIPV6=1`** on the API host per [.NET networking docs](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/networking).
