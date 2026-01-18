# Test-Driven Development Guidelines

## Context

You are following Test-Driven Development (TDD). Tests must be written BEFORE implementation code.

## Critical Rules

- NEVER write implementation code before tests
- NEVER skip the "verify test fails" step
- NEVER write more code than needed to pass tests

## Workflow

Follow these steps exactly:

### 1. WRITE TEST FIRST
- Create test method with descriptive name
- Add issue reference in XML comment
- Write test that exercises the expected behavior

```csharp
/// <summary>
/// Tests that Save throws when database unavailable.
/// Issue: #42
/// </summary>
[Fact]
public async Task Save_WhenDatabaseUnavailable_ThrowsException()
{
    // Arrange
    var service = new UserService(mockDb.Object);
    mockDb.Setup(x => x.IsAvailable).Returns(false);

    // Act & Assert
    await Assert.ThrowsAsync<DatabaseException>(
        () => service.SaveAsync(user));
}
```

### 2. RUN TEST - VERIFY IT FAILS
```bash
dotnet test --filter "Name~Save_WhenDatabaseUnavailable"
```
If test passes, your test is wrong - fix it first.

### 3. WRITE MINIMUM CODE
- Only write enough code to make the test pass
- Do not add extra features or error handling

### 4. RUN TEST - VERIFY IT PASSES
```bash
dotnet test --filter "Name~Save_WhenDatabaseUnavailable"
```

### 5. REFACTOR (optional)
- Clean up code while keeping tests passing
- Run tests after each refactor step

### 6. COMMIT
- Commit test and implementation together

## Naming Convention

Test method names follow: `MethodName_Scenario_ExpectedResult`

Examples:
- `Save_WithValidUser_ReturnsSuccess`
- `Save_WhenDatabaseUnavailable_ThrowsException`
- `Calculate_WithNegativeInput_ReturnsZero`
