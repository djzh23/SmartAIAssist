-- Learning memory (insights JSON) in PostgreSQL (optional cutover from Redis via DatabaseFeatures:LearningMemoryStorage).
-- Requires 001_initial_app_users.sql applied first.

CREATE TABLE IF NOT EXISTS learning_memories (
    clerk_user_id TEXT NOT NULL PRIMARY KEY REFERENCES app_users (clerk_user_id) ON DELETE CASCADE,
    memory_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE learning_memories IS 'Per-user learning insights; mirrors Redis key learning:{userId} JSON.';
