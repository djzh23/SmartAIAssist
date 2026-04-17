-- Token usage aggregates (replaces Upstash keys tokens:daily:*, tokens:global:daily:*).
-- Optional FK to app_users omitted for anon keys like anon:* in usage counters.

CREATE TABLE IF NOT EXISTS token_usage_global_daily (
    usage_date DATE NOT NULL PRIMARY KEY,
    message_count BIGINT NOT NULL DEFAULT 0,
    input_tokens BIGINT NOT NULL DEFAULT 0,
    output_tokens BIGINT NOT NULL DEFAULT 0,
    cost_usd NUMERIC(18, 6) NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS token_usage_daily_user (
    clerk_user_id TEXT NOT NULL,
    usage_date DATE NOT NULL,
    message_count BIGINT NOT NULL DEFAULT 0,
    input_tokens BIGINT NOT NULL DEFAULT 0,
    output_tokens BIGINT NOT NULL DEFAULT 0,
    cost_usd NUMERIC(18, 6) NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (clerk_user_id, usage_date)
);

CREATE INDEX IF NOT EXISTS idx_token_usage_daily_user_date
    ON token_usage_daily_user (usage_date);

CREATE INDEX IF NOT EXISTS idx_token_usage_daily_user_user_date
    ON token_usage_daily_user (clerk_user_id, usage_date DESC);

CREATE TABLE IF NOT EXISTS token_usage_daily_user_model (
    clerk_user_id TEXT NOT NULL,
    usage_date DATE NOT NULL,
    model_key TEXT NOT NULL,
    message_count BIGINT NOT NULL DEFAULT 0,
    input_tokens BIGINT NOT NULL DEFAULT 0,
    output_tokens BIGINT NOT NULL DEFAULT 0,
    cost_usd NUMERIC(18, 6) NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (clerk_user_id, usage_date, model_key)
);

CREATE INDEX IF NOT EXISTS idx_token_usage_daily_user_model_date
    ON token_usage_daily_user_model (usage_date);

CREATE TABLE IF NOT EXISTS token_usage_daily_user_tool (
    clerk_user_id TEXT NOT NULL,
    usage_date DATE NOT NULL,
    tool TEXT NOT NULL,
    message_count BIGINT NOT NULL DEFAULT 0,
    input_tokens BIGINT NOT NULL DEFAULT 0,
    output_tokens BIGINT NOT NULL DEFAULT 0,
    cost_usd NUMERIC(18, 6) NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (clerk_user_id, usage_date, tool)
);

CREATE INDEX IF NOT EXISTS idx_token_usage_daily_user_tool_date
    ON token_usage_daily_user_tool (usage_date);

-- Users who have at least one tracked token event (mirrors tokens:users:registered set).
CREATE TABLE IF NOT EXISTS token_usage_registered_users (
    clerk_user_id TEXT NOT NULL PRIMARY KEY,
    first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE token_usage_global_daily IS 'Aggregated global metrics per UTC day.';
