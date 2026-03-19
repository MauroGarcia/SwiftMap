# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-03-18

### Added
- `MapAsync<TSource, TDestination>` on `IMapper` and `Mapper` for lazy mapping of `IAsyncEnumerable<T>` streams without materializing the full result set
- Mapping delegate is resolved once and reused for the entire async enumeration
- Full cancellation support via `CancellationToken`

### Fixed
- Corrected repository and package URLs in `SwiftMap.csproj` (were pointing to `YOUR_USERNAME` placeholder)

## [1.0.0] - 2026-03-16

### Added
- Zero-allocation object mapper using expression tree compilation
- Roslyn Source Generator for compile-time mapping code generation
- `IMapper` interface with `Map<TSource, TDestination>` support
- Dependency injection integration via `AddSwiftMap()` extension method
- Support for nested object mapping
- Support for collection mapping (`IEnumerable<T>`, `List<T>`, arrays)
- `[MapIgnore]` attribute to exclude properties from mapping
- `[MapFrom]` attribute for property name remapping
- XML documentation for all public APIs

[Unreleased]: https://github.com/MauroGarcia/SwiftMap/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/MauroGarcia/SwiftMap/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/MauroGarcia/SwiftMap/releases/tag/v1.0.0
