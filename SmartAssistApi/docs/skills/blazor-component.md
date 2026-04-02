# Skill: Create Blazor Component

Use this skill when creating any new Blazor component.

## Steps

1. Create file: Components/{Name}.razor
2. Add parameters with [Parameter] attribute
3. Add @code block with minimal logic
4. Inject services with @inject
5. Handle loading state with a bool isLoading field
6. Handle errors with a string? errorMessage field

## Template

```razor
@* Components/{Name}.razor *@
<div class="component-wrapper">
    @if (isLoading)
    {
        <p>Laden...</p>
    }
    else if (errorMessage is not null)
    {
        <p class="error">@errorMessage</p>
    }
    else
    {
        @* actual content *@
    }
</div>

@code {
    [Parameter] public string Title { get; set; } = "";

    private bool isLoading = false;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        isLoading = true;
        try { await LoadData(); }
        catch (Exception ex) { errorMessage = ex.Message; }
        finally { isLoading = false; }
    }

    private async Task LoadData() { }
}
```
