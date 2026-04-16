-- SmartAssist: root table for Clerk user ids (FK target for tenant data).
-- Apply in Supabase SQL Editor or via your migration runner.

CREATE TABLE IF NOT EXISTS app_users (
    clerk_user_id TEXT PRIMARY KEY,
    created_at TIMESTAMPTZ NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);

COMMENT ON TABLE app_users IS 'Clerk user id (sub). Rows may be created lazily from the API on first write.';
