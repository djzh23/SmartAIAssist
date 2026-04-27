-- Performance indexes — covering and partial indexes for the three slow pages:
-- Bewerbungen, CV.Studio overview, and Übersicht.
-- Run after 011_cv_resume_categories.sql.

-- ── job_applications ─────────────────────────────────────────────────────────

-- Point-lookup composite for SaveAllAsync upsert loop (clerk_user_id + application_id).
-- The existing idx_job_applications_user_updated covers ORDER BY; this covers WHERE + PK access.
CREATE INDEX IF NOT EXISTS IX_job_applications_user_app
    ON job_applications (clerk_user_id, application_id);

-- ── resumes (CV.Studio) ──────────────────────────────────────────────────────

-- Covering index for the summary list query: supplies all non-JSON columns used by
-- the list endpoint without a heap fetch, so only the JSONB extraction sub-expression
-- touches the main table. INCLUDE columns are read from the index leaf pages directly.
CREATE INDEX IF NOT EXISTS IX_resumes_summary_covering
    ON resumes (clerk_user_id, updated_at_utc DESC)
    INCLUDE (id, title, template_key, linked_job_application_id,
             target_company, target_role, notes);

-- Existing EF Core index: (clerk_user_id, updated_at_utc) — kept but superseded by above.
-- CREATE INDEX IF NOT EXISTS "IX_resumes_ClerkUserId_UpdatedAtUtc" already created by EF migrations.

-- ── resume_versions (snapshot list sidebar) ──────────────────────────────────

-- Covering index for the metadata-only list: supplies all projected columns without
-- loading content_json from the main table pages.
CREATE INDEX IF NOT EXISTS IX_resume_versions_metadata_covering
    ON resume_versions (resume_id, version_number DESC)
    INCLUDE (id, label, created_at_utc);

-- ── career_profiles ──────────────────────────────────────────────────────────

-- The table uses clerk_user_id as PRIMARY KEY (implicit btree index) so GetProfile is already
-- a PK lookup. No additional index needed. The SELECT projection added in code already avoids
-- fetching cv_raw_text at the application layer.

-- ── cv_user_categories & assignments (new in 011) ────────────────────────────

-- Already created in 011_cv_resume_categories.sql:
--   IX_cv_user_categories_user  ON cv_user_categories (clerk_user_id)
--   IX_cv_resume_category_assignments_user ON cv_resume_category_assignments (clerk_user_id)
