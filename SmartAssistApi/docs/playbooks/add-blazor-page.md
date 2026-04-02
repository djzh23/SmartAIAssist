# Playbook: Add Blazor Page

## Step 1 — Create page

Path: `SmartAssistApi.Client/Pages/{Name}.razor`

Add `@page "/{route}"` directive at the top.

## Step 2 — Create API service if needed

Path: `SmartAssistApi.Client/Services/{Name}Service.cs`

Inject `HttpClient`, wrap all API calls here. No direct `HttpClient` usage in pages.

## Step 3 — Register service

In `SmartAssistApi.Client/Program.cs`:

```csharp
builder.Services.AddScoped<{Name}Service>();
```

## Step 4 — Add navigation link

In `Shared/NavMenu.razor`, add a `NavLink` entry:

```razor
<div class="nav-item px-3">
    <NavLink class="nav-link" href="{route}">
        <span class="bi bi-{icon}" aria-hidden="true"></span> {Name}
    </NavLink>
</div>
```

## Step 5 — Test manually

```bash
dotnet run --project SmartAssistApi.Client
```

Navigate to the page and verify loading, error, and happy-path states all render correctly.

## Step 6 — Commit

```bash
git commit -m "feat: add {name} page"
```
