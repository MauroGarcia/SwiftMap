# Contributing to SwiftMap

Contributions are welcome — bug fixes, new features, performance improvements, and documentation.

## Getting started

```bash
git clone https://github.com/MauroGarcia/SwiftMap
cd SwiftMap
dotnet build
dotnet test
```

## Project structure

```
src/SwiftMap/                  # Runtime mapper (expression trees)
src/SwiftMap.SourceGenerator/  # Roslyn source generator
tests/                         # Unit and integration tests
benchmarks/                    # BenchmarkDotNet suite
```

## Making changes

1. Fork the repository and create a feature branch:
   ```bash
   git checkout -b feature/my-feature
   ```

2. Make your changes and add or update tests.

3. Run the tests:
   ```bash
   dotnet test
   ```

4. If your change touches performance-sensitive code, run the benchmarks to verify there is no regression:
   ```bash
   dotnet run -c Release --project benchmarks/SwiftMap.Benchmarks
   ```

5. Open a pull request with a clear description of what changed and why.

## Guidelines

- Keep PRs focused — one concern per pull request
- For significant changes, open an issue first to discuss the approach
- Public API additions should be reflected in the README
- Match the existing code style

## Reporting bugs

Use the [bug report template](.github/ISSUE_TEMPLATE/bug_report.md) and include a minimal code example that reproduces the issue.
