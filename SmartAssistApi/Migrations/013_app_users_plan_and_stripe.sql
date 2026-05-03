-- Consolidate plan + Stripe customer mapping into app_users.
-- After this migration, user_plan is kept read-only as a fallback view;
-- new writes go directly to app_users.plan.

-- Add new columns to app_users
ALTER TABLE app_users
    ADD COLUMN IF NOT EXISTS plan TEXT NOT NULL DEFAULT 'free',
    ADD COLUMN IF NOT EXISTS plan_updated_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS stripe_customer_id TEXT,
    ADD COLUMN IF NOT EXISTS last_active_at TIMESTAMPTZ NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    ADD COLUMN IF NOT EXISTS onboarding_state TEXT NOT NULL DEFAULT 'pending';

-- Backfill plan from user_plan table
UPDATE app_users au
SET plan = up.plan,
    plan_updated_at = up.updated_at
FROM user_plan up
WHERE au.clerk_user_id = up.clerk_user_id
  AND up.plan IS NOT NULL
  AND up.plan != 'free';

-- Index for admin queries and Stripe lookups
CREATE INDEX IF NOT EXISTS idx_app_users_plan ON app_users (plan);
CREATE UNIQUE INDEX IF NOT EXISTS idx_app_users_stripe_customer
    ON app_users (stripe_customer_id) WHERE stripe_customer_id IS NOT NULL;

COMMENT ON COLUMN app_users.plan IS 'Subscription plan: free | premium | pro. Source of truth (replaces user_plan table).';
COMMENT ON COLUMN app_users.stripe_customer_id IS 'Stripe customer ID (cus_xxx). Moved from Redis for durability.';
COMMENT ON COLUMN app_users.onboarding_state IS 'pending | completed | skipped.';
