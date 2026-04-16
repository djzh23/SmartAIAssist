-- Chat notes in PostgreSQL (optional cutover from Redis via DatabaseFeatures:ChatNotesStorage).
-- Requires 001_initial_app_users.sql applied first.

CREATE TABLE IF NOT EXISTS chat_notes (
    id TEXT PRIMARY KEY,
    clerk_user_id TEXT NOT NULL REFERENCES app_users (clerk_user_id) ON DELETE CASCADE,
    title VARCHAR(120) NOT NULL,
    body TEXT NOT NULL,
    tags TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
    source_json JSONB,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_chat_notes_user_updated
    ON chat_notes (clerk_user_id, updated_at DESC);

COMMENT ON TABLE chat_notes IS 'User-saved chat notes; order by updated_at DESC matches Redis index newest-first.';
