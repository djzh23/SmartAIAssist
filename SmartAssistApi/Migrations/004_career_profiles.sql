-- Career profiles (Phase B). Requires app_users (001).
-- One row per user; profile_json mirrors API CareerProfile (camelCase); cv_raw_text mirrors Redis profile:{userId}:cv_raw.

CREATE TABLE IF NOT EXISTS career_profiles (
    clerk_user_id TEXT NOT NULL PRIMARY KEY
        REFERENCES app_users (clerk_user_id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    profile_json JSONB NOT NULL,
    cv_raw_text TEXT,
    cache_version BIGINT NOT NULL DEFAULT 0,
    CONSTRAINT chk_career_profiles_profile_json_object
        CHECK (jsonb_typeof(profile_json) = 'object')
);

CREATE INDEX IF NOT EXISTS idx_career_profiles_updated
    ON career_profiles (updated_at DESC);

COMMENT ON TABLE career_profiles IS 'Career profile document per user; profile_json matches GET/PUT /api/profile shape.';
COMMENT ON COLUMN career_profiles.profile_json IS 'Serialized CareerProfile; cvRawText inside is truncated (max 3000 in app).';
COMMENT ON COLUMN career_profiles.cv_raw_text IS 'Full CV text when uploaded (mirrors Redis profile:{userId}:cv_raw); optional.';
COMMENT ON COLUMN career_profiles.cache_version IS 'Incremented on each save; drives prompt cache key version (replaces Redis profile_version ticks).';
