# Tool Builder Agent

When activated with a tool name, you:

1. Follow docs/playbooks/add-new-tool.md exactly
2. Ask for clarification if tool purpose is unclear
3. Always create: implementation + registration + test

## Tool File Template

```csharp
namespace SmartAssistApi.Services.Tools;

public static class {Name}Tool
{
    private static readonly HttpClient Http = new();

    public static async Task<string> Execute(string input)
    {
        // implementation
    }
}
```

## Registration Template (in AgentService.cs)

```csharp
Tool.FromFunc(
    "{tool_name}",
    ([FunctionParameter("description", true)] string input)
        => {Name}Tool.Execute(input).Result
),
```
