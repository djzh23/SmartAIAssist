-- Chat session index + transcripts in PostgreSQL (optional cutover from Redis via DatabaseFeatures:ChatSessionStorage).
-- Requires 001_initial_app_users.sql applied first.

CREATE TABLE IF NOT EXISTS chat_sessions (
    clerk_user_id TEXT NOT NULL REFERENCES app_users (clerk_user_id) ON DELETE CASCADE,
    session_id TEXT NOT NULL,
    title VARCHAR(120) NOT NULL,
    tool_type VARCHAR(40) NOT NULL DEFAULT 'general',
    created_at TIMESTAMPTZ NOT NULL,
    last_message_at TIMESTAMPTZ NOT NULL,
    message_count INT NOT NULL DEFAULT 0,
    display_order INT NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (clerk_user_id, session_id)
);

CREATE INDEX IF NOT EXISTS idx_chat_sessions_user_order
    ON chat_sessions (clerk_user_id, display_order);

COMMENT ON TABLE chat_sessions IS 'Per-user chat tabs; display_order matches sidebar order (0 = first).';

CREATE TABLE IF NOT EXISTS chat_transcripts (
    clerk_user_id TEXT NOT NULL,
    session_id TEXT NOT NULL,
    tool_type VARCHAR(40) NOT NULL DEFAULT 'general',
    messages_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (clerk_user_id, session_id),
    FOREIGN KEY (clerk_user_id, session_id)
        REFERENCES chat_sessions (clerk_user_id, session_id) ON DELETE CASCADE
);

COMMENT ON TABLE chat_transcripts IS 'Message arrays per session; mirrors Redis chat_transcript:{userId}:{sessionId} payload.messages.';
