# Skill: Blazor Design System

This is the single source of truth for all visual decisions.

## Color Palette
Define in wwwroot/css/app.css as CSS variables:

:root {
  /* Primary */
  --color-primary: #534AB7;
  --color-primary-hover: #3C3489;
  --color-primary-light: #EEEDFE;
  
  /* Surface */
  --color-bg: #ffffff;
  --color-bg-secondary: #F8F8F8;
  --color-sidebar: #1a1a2e;
  --color-sidebar-text: #e0e0e0;
  
  /* Text */
  --color-text: #1a1a1a;
  --color-text-muted: #6b7280;
  
  /* Message bubbles */
  --color-bubble-user: #534AB7;
  --color-bubble-user-text: #ffffff;
  --color-bubble-assistant: #F3F4F6;
  --color-bubble-assistant-text: #1a1a1a;
  
  /* Tool pill */
  --color-tool-bg: #E1F5EE;
  --color-tool-text: #085041;
  --color-tool-border: #5DCAA5;
  
  /* Status */
  --color-error: #ef4444;
  --color-success: #22c55e;
  
  /* Spacing */
  --space-xs: 4px;
  --space-sm: 8px;
  --space-md: 16px;
  --space-lg: 24px;
  --space-xl: 32px;
  
  /* Border */
  --radius-sm: 6px;
  --radius-md: 12px;
  --radius-lg: 20px;
  --radius-pill: 999px;
  
  /* Shadow */
  --shadow-sm: 0 1px 3px rgba(0,0,0,0.08);
  --shadow-md: 0 4px 12px rgba(0,0,0,0.1);
}

## Typography
- Font: system-ui, -apple-system, sans-serif
- Base size: 14px
- Message text: 14px, line-height 1.6
- Sidebar items: 13px
- Timestamps: 11px, color: var(--color-text-muted)
- Tool pills: 11px, font-weight 500

## Component Patterns

### Message Bubble (user)
.bubble-user {
  background: var(--color-bubble-user);
  color: var(--color-bubble-user-text);
  border-radius: var(--radius-lg) var(--radius-lg) var(--radius-sm) var(--radius-lg);
  padding: var(--space-sm) var(--space-md);
  max-width: 75%;
  align-self: flex-end;
}

### Message Bubble (assistant)
.bubble-assistant {
  background: var(--color-bubble-assistant);
  color: var(--color-bubble-assistant-text);
  border-radius: var(--radius-lg) var(--radius-lg) var(--radius-lg) var(--radius-sm);
  padding: var(--space-sm) var(--space-md);
  max-width: 80%;
  align-self: flex-start;
}

### Tool Pill
.tool-pill {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  background: var(--color-tool-bg);
  color: var(--color-tool-text);
  border: 1px solid var(--color-tool-border);
  border-radius: var(--radius-pill);
  padding: 2px 10px;
  font-size: 11px;
  font-weight: 500;
  margin-top: 4px;
}

### Loading Dots
.loading-dots span {
  animation: bounce 1.2s infinite;
  display: inline-block;
}

### Send Button
.btn-send {
  background: var(--color-primary);
  color: white;
  border: none;
  border-radius: var(--radius-sm);
  padding: 8px 20px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.15s;
}
.btn-send:hover { background: var(--color-primary-hover); }
.btn-send:disabled { opacity: 0.5; cursor: default; }

## Always Use Variables — Never Hardcode
WRONG: color: #534AB7
RIGHT: color: var(--color-primary)
