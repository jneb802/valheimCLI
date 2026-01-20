# Contributing to WarpValheimCLI

Thank you for your interest in contributing to **WarpValheimCLI**! We welcome contributions from the community and are
pleased that you want to help us improve the project.

This document identifies the standards and workflows we use to ensure high-quality code and a maintainable project.
Please take a moment to read through it before sending a pull request.

## Table of Contents

- [Coding Guidelines](#coding-guidelines)
- [Submission Guidelines](#submission-guidelines)

---

## Coding Guidelines

We strive for code that is clean, readable, and consistent.

### Formatting & Style

- Please ensure all code is indented using **4 spaces**.
- For control structures and blocks we use the **Allman style** placing opening braces on their own new line.

```csharp
// Correct
if (condition)
{
    DoSomething();
}

// Incorrect
if (condition) {
    DoSomething();
}
```

### Naming Conventions

Consistency in naming allows us to instantly recognize the scope and type of a variable.

We generally follow standard.NET naming conventions:

- **PascalCase** for classes, structs, enums, constants, and all public members (methods, properties).
- Interfaces should be prefixed with an `I`.
- Private fields use **\_camelCase** (an underscore prefix followed by camelCase),
- Parameters and local variables use standard **camelCase** without prefixes.

| Item                         | Style       | Example                             |
| :--------------------------- | :---------- | :---------------------------------- |
| **Classes / Public Members** | PascalCase  | `CommandLineParser`, `ProcessInput` |
| **Private Fields**           | \_camelCase | `_logSource`                        |
| **Parameters / Locals**      | camelCase   | `inputString`                       |

### Project Structure

Namespaces should predominantly use the root namespace `WarpValheimCLI`. Sub-namespaces should be reserved only for
distinct features that require separation.

### Variables & Typing

When declaring variables, please use **explicit typing** in all cases and DO NOT use `var`.

---

## Submission Guidelines

When you are ready to contribute, please work on a separate feature branch rather than directly on main. We ask that
commit messages be clear and imperative, describing _what_ the change does.

Before submitting a Pull Request, please verify that your solution builds without warnings. In your PR description,
kindly explain what your changes do and why they are necessary to help reviewers understand your context.

Thank you for helping make WarpValheimCLI better!
