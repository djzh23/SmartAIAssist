CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS usage_records (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id TEXT NOT NULL,
    tool_type TEXT NOT NULL,
    session_id TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    input_tokens INT NOT NULL DEFAULT 0,
    output_tokens INT NOT NULL DEFAULT 0,
    cache_creation_tokens INT NOT NULL DEFAULT 0,
    cache_read_tokens INT NOT NULL DEFAULT 0,
    model TEXT,
    response_time_ms INT,
    estimated_cost_usd NUMERIC(10, 6)
);

CREATE INDEX IF NOT EXISTS idx_usage_records_user
    ON usage_records (user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_usage_records_daily
    ON usage_records (created_at DESC);

CREATE INDEX IF NOT EXISTS idx_usage_records_tool
    ON usage_records (tool_type, created_at DESC);
