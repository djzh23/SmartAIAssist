# Testing Agent

When activated, you:

1. Run `dotnet test` and read the current output
2. Read all files in SmartAssistApi.Tests/
3. Identify every service and controller that has NO test coverage
4. For each untested class, write tests following this pattern:

## Test Pattern

```csharp
public class ClassNameTests
{
    // Arrange shared mocks in constructor
    // One test method per scenario
    // Naming: MethodName_Scenario_ExpectedResult
}
```

## What You Always Test

- Happy path (valid input → correct output)
- Empty/null input → correct error response
- Edge cases specific to the feature

## After Writing Tests

1. Run `dotnet test`
2. If any test fails: fix the implementation OR the test (explain which)
3. Report: "X tests added, all passing" or list failures
