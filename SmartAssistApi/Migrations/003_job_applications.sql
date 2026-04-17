-- Job applications (Phase A). Requires app_users (001).
-- One row per application; API lists newest first by updated_at (see index).

CREATE TABLE IF NOT EXISTS job_applications (
    clerk_user_id TEXT NOT NULL
        REFERENCES app_users (clerk_user_id) ON DELETE CASCADE,
    application_id TEXT NOT NULL,
    job_title VARCHAR(300) NOT NULL,
    company VARCHAR(300) NOT NULL,
    job_url TEXT,
    job_description TEXT,
    status VARCHAR(80) NOT NULL DEFAULT 'draft',
    status_updated_at TIMESTAMPTZ NOT NULL,
    tailored_cv_notes TEXT,
    cover_letter_text TEXT,
    interview_notes TEXT,
    -- Matches JobApplicationDocument.Timeline: ordered array of { date, description, note? }
    timeline JSONB NOT NULL DEFAULT '[]'::jsonb,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    analysis_session_id TEXT,
    interview_session_id TEXT,
    CONSTRAINT pk_job_applications PRIMARY KEY (clerk_user_id, application_id),
    CONSTRAINT chk_job_applications_timeline_is_array
        CHECK (jsonb_typeof(timeline) = 'array')
);

CREATE INDEX IF NOT EXISTS idx_job_applications_user_updated
    ON job_applications (clerk_user_id, updated_at DESC);

COMMENT ON TABLE job_applications IS 'Per-user job applications; API lists newest first (ORDER BY updated_at DESC).';
COMMENT ON COLUMN job_applications.application_id IS 'Client-generated id (12 hex chars), unique per clerk_user_id.';
COMMENT ON COLUMN job_applications.timeline IS 'JSON array of timeline events; order preserved for UI.';
