# Database migrations (Supabase / PostgreSQL)

Run SQL files **in order** in the Supabase SQL Editor (or your CI migration step) against the project database.

| File | Purpose |
|------|---------|
| `001_initial_app_users.sql` | Root `app_users` table for Clerk IDs |
| `002_chat_notes.sql` | `chat_notes` + FK to `app_users` |

## Backfill chat notes from Redis (manual)

When `DatabaseFeatures:ChatNotesStorage` is switched to `postgres`, run a one-off backfill (script or admin tool) that:

1. For each user key pattern `chatnotes:v2:{userId}:index`, read note ids.
2. For each id, read `chatnotes:v2:{userId}:n:{noteId}` JSON.
3. `INSERT INTO app_users (clerk_user_id) VALUES ($userId) ON CONFLICT DO NOTHING`.
4. `INSERT INTO chat_notes (...) ON CONFLICT (id) DO UPDATE` from JSON fields.

Until backfill completes, new writes can go to Postgres while old data remains in Redis — prefer a maintenance window or dual-write (not implemented by default).
