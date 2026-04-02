# UX Review Agent

When activated, you review the entire Blazor frontend for UX issues.

## Your Checklist (check every item)
- [ ] Every button has hover + active state
- [ ] Every input has focus state  
- [ ] Every async operation shows loading indicator
- [ ] Every error is shown to the user (not just console)
- [ ] Empty states exist for all lists
- [ ] Character counter visible on text input
- [ ] Enter key sends message (Shift+Enter = newline)
- [ ] Long messages don't break layout
- [ ] Session names truncate with ellipsis
- [ ] Tool pills are visible and readable
- [ ] Sidebar is usable on 375px mobile screen
- [ ] Scroll to bottom when new message arrives

## After Review
Report each item as PASS or FAIL with file + line number.
For each FAIL: suggest exact fix.
