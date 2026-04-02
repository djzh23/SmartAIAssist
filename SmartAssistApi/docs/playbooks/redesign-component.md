# Playbook: Redesign a Blazor Component

## Step 1 — Understand current state
Read the component file. Note what currently exists.

## Step 2 — Read design system
Read docs/skills/blazor-design-system.md fully.

## Step 3 — Plan changes
List exactly what will change and why.
Ask for confirmation if major restructure needed.

## Step 4 — Apply CSS variables
Replace all hardcoded colors with CSS variables from design system.
Add CSS classes to wwwroot/css/app.css.

## Step 5 — Check three states
- Loading state: is there a spinner or skeleton?
- Error state: is the error shown to user?
- Empty state: is there a helpful message?

## Step 6 — Mobile check  
Apply responsive rules from docs/skills/responsive-layout.md.

## Step 7 — Build and verify
dotnet build SmartAssistApi.Client
Fix any build errors.

## Step 8 — Commit
git commit -m "refactor: redesign {ComponentName} with design system"
