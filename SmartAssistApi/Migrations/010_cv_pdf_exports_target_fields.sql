-- CV.Studio: add target company/role to pdf export records for grouped display.
-- Columns are nullable; existing rows keep NULL (legacy exports without context).

ALTER TABLE cv_pdf_exports
    ADD COLUMN IF NOT EXISTS target_company VARCHAR(300) NULL,
    ADD COLUMN IF NOT EXISTS target_role    VARCHAR(300) NULL;

COMMENT ON COLUMN cv_pdf_exports.target_company IS 'Denormalized company name from the linked resume at export time.';
COMMENT ON COLUMN cv_pdf_exports.target_role    IS 'Denormalized role name from the linked resume at export time.';
