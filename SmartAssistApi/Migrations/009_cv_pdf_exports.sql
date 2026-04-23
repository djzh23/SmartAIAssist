-- CV.Studio: tracked PDF exports per user (quota + delete to free slots).
-- Requires app_users (001). resume_id references logical CvStudio resume UUID (no FK to cv.studio tables).

CREATE TABLE IF NOT EXISTS cv_pdf_exports (
    id UUID NOT NULL DEFAULT gen_random_uuid(),
    clerk_user_id TEXT NOT NULL
        REFERENCES app_users (clerk_user_id) ON DELETE CASCADE,
    resume_id UUID NOT NULL,
    version_id UUID NULL,
    design VARCHAR(8) NOT NULL DEFAULT 'A',
    file_label TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    storage_object_path TEXT NULL,
    CONSTRAINT pk_cv_pdf_exports PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS idx_cv_pdf_exports_user_created
    ON cv_pdf_exports (clerk_user_id, created_at DESC);

COMMENT ON TABLE cv_pdf_exports IS 'One row per PDF download tracked for CV.Studio plan limits; delete row to free a slot.';
COMMENT ON COLUMN cv_pdf_exports.storage_object_path IS 'Optional Supabase Storage object path when file persistence is enabled.';
