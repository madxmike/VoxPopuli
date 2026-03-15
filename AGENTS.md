# Coding Standards

## C# Style Rules

- All control flow statements (`if`, `else`, `for`, `foreach`, `while`, `do`, `using`) MUST use brackets, even for single-line bodies.

```csharp
// ✅ Correct
if (condition)
{
    DoSomething();
}

// ❌ Wrong
if (condition)
    DoSomething();
```
