-- CV.Studio: user-defined categories for master CVs, stored server-side.
-- Replaces the prior browser-localStorage approach.

CREATE TABLE IF NOT EXISTS cv_user_categories (
    id              uuid            PRIMARY KEY DEFAULT gen_random_uuid(),
    clerk_user_id   varchar(128)    NOT NULL,
    name            varchar(80)     NOT NULL,
    sort_order      integer         NOT NULL DEFAULT 0,
    created_at_utc  timestamptz     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS IX_cv_user_categories_user
    ON cv_user_categories (clerk_user_id);

-- resume_id is a logical reference to resumes.id (cross-DbContext, no FK constraint).
CREATE TABLE IF NOT EXISTS cv_resume_category_assignments (
    resume_id       uuid            NOT NULL,
    clerk_user_id   varchar(128)    NOT NULL,
    category_id     uuid            NOT NULL REFERENCES cv_user_categories(id) ON DELETE CASCADE,
    PRIMARY KEY (resume_id)
);

CREATE INDEX IF NOT EXISTS IX_cv_resume_category_assignments_user
    ON cv_resume_category_assignments (clerk_user_id);
