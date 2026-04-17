-- Daily message counts for rate limits (usage:{userId}:{date}) and plan (plan:{userId}).
-- clerk_user_id may be anon:* for anonymous counters.

CREATE TABLE IF NOT EXISTS user_usage_daily (
    clerk_user_id TEXT NOT NULL,
    usage_date DATE NOT NULL,
    usage_count INT NOT NULL DEFAULT 0,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (clerk_user_id, usage_date)
);

CREATE INDEX IF NOT EXISTS idx_user_usage_daily_date ON user_usage_daily (usage_date);

CREATE TABLE IF NOT EXISTS user_plan (
    clerk_user_id TEXT NOT NULL PRIMARY KEY,
    plan TEXT NOT NULL DEFAULT 'free',
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE user_usage_daily IS 'Per-day request counts for CheckAndIncrement (signed-in and anon:* keys).';
COMMENT ON TABLE user_plan IS 'Subscription plan label for signed-in users (replaces plan:{userId} in Redis).';
