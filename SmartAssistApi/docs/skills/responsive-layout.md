# Skill: Responsive Layout

## Breakpoints
- Mobile: < 768px
- Tablet: 768px - 1024px  
- Desktop: > 1024px

## Chat App Layout Rules

### Desktop (default)
display: grid;
grid-template-columns: 240px 1fr;
height: 100vh;

### Mobile (< 768px)
- Sidebar hides: display: none or transform: translateX(-100%)
- Hamburger button appears in header
- Full width chat area
- Input stays at bottom (position: sticky)

## CSS Pattern
@media (max-width: 768px) {
  .sidebar { display: none; }
  .hamburger-btn { display: flex; }
  .chat-area { grid-column: 1 / -1; }
}
