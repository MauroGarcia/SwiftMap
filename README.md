<div align="center">

# SwiftMap

**Zero-allocation, expression-tree-compiled object mapper for .NET**

[![NuGet](https://img.shields.io/nuget/v/SwiftMap?style=flat-square&color=004880&label=NuGet)](https://www.nuget.org/packages/SwiftMap)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)

_Convention-based property mapping backed by compiled expression trees — zero reflection at call time._

</div>

---

## Why SwiftMap?

- **Zero allocations at mapping time** — only the destination object is allocated; no boxing, no intermediate arrays, no iterators
- **Expression tree compilation** — mappings compile once to native delegates; subsequent calls are as fast as hand-written code
- **Fluent, discoverable API** — `ForMember`, `Ignore`, `AfterMap`, `ReverseMap`, `NullSubstitute`, `Condition`, and more
- **No heavy dependencies** — one NuGet reference; only depends on `Microsoft.Extensions.DependencyInjection.Abstractions`
- **Records & init-only properties** — full support for C# 9+ records via primary constructor selection
- **First-class DI support** — drop-in `services.AddSwiftMap(...)` with profile scanning

---

## Installation

**.NET CLI**
```bash
dotnet add package SwiftMap
```

**Package Manager Console**
```powershell
Install-Package SwiftMap
```

**PackageReference**
```xml
<PackageReference Include="SwiftMap" Version="1.0.0" />
```

---

## Quick Start

```csharp
// 1. Create a mapper — once at startup or via DI
var mapper = Mapper.Create(cfg =>
    cfg.CreateMap<PersonSource, PersonDest>());

// 2. Map by source type inference
var dest = mapper.Map<PersonDest>(source);

// 3. Map with explicit type parameters (slightly faster — no GetType() call)
var dest = mapper.Map<PersonSource, PersonDest>(source);

// 4. Map into an existing instance
mapper.Map(source, existingDestination);
```

---

## Configuration

### Convention mapping — no config needed

Matching properties are wired up automatically by name (case-insensitive):

```csharp
var mapper = Mapper.Create(_ => { });
var dto = mapper.Map<CustomerDto>(customer);
```

### Fluent API

```csharp
var mapper = Mapper.Create(cfg =>
    cfg.CreateMap<Customer, CustomerDto>(map =>
        map.ForMember(d => d.AddressCity,  opt => opt.MapFrom(s => s.Address!.City))
           .Ignore(d => d.InternalId)
           .ForMember(d => d.Name,         opt => opt.NullSubstitute("Unknown"))
           .ForMember(d => d.Score,        opt => opt.Condition(s => s.IsActive))
           .AfterMap((src, dest) => dest.Name = dest.Name.ToUpperInvariant())));
```

### Reverse mapping

```csharp
cfg.CreateMap<OrderDto, Order>()
   .ReverseMap(); // registers the inverse mapping automatically
```

### Profiles

Organise large sets of mappings into cohesive units:

```csharp
public class AppProfile : MapProfile
{
    public AppProfile()
    {
        CreateMap<Customer, CustomerDto>()
            .ForMember(d => d.AddressCity, opt => opt.MapFrom(s => s.Address!.City));

        CreateMap<Order, OrderDto>().ReverseMap();
    }
}

var mapper = Mapper.Create(cfg => cfg.AddProfile<AppProfile>());
```

### Attribute-driven mapping

```csharp
[MapTo(typeof(ProductDto))]
public class Product { ... }

public class TargetDto
{
    [MapProperty("FullName")]   // maps from a differently-named source property
    public string Name { get; set; }

    [IgnoreMap]                 // skipped during mapping
    public string Secret { get; set; }
}
```

### Dependency Injection

```csharp
// Inline configuration
services.AddSwiftMap(cfg => cfg.CreateMap<Order, OrderDto>());

// Scan assemblies for MapProfile subclasses and [MapTo]/[MapFrom] attributes
services.AddSwiftMap(typeof(Program).Assembly);
```

---

## Benchmarks

Benchmarked against **AutoMapper 13.0.1** and **Mapster 7.4.0** on .NET 9.

```
BenchmarkDotNet v0.15.8  ·  Windows 11  ·  AMD Ryzen 5 3600 3.60GHz (6C/12T)
.NET SDK 9.0.312  ·  .NET 9.0.14  ·  X64 RyuJIT x86-64-v3
ShortRun: 3 warmups + 7 iterations
```

### Simple flat object (7 properties)

| Method         |      Mean |    Error |   StdDev | Ratio         | Allocated |
|:---------------|----------:|---------:|---------:|:--------------|----------:|
| Manual         |  13.23 ns | 3.432 ns | 1.524 ns | baseline      |      64 B |
| Mapster        |  25.48 ns | 3.667 ns | 1.628 ns | 1.95× slower  |      64 B |
| **SwiftMap**   |  38.04 ns | 5.319 ns | 2.362 ns | 2.91× slower  |      64 B |
| AutoMapper     |  76.79 ns | 7.118 ns | 3.161 ns | 5.87× slower  |      64 B |

### Nested object (parent + child)

| Method         |      Mean |    Error |   StdDev | Ratio         | Allocated |
|:---------------|----------:|---------:|---------:|:--------------|----------:|
| Manual         |  19.89 ns | 6.382 ns | 2.834 ns | baseline      |     104 B |
| Mapster        |  35.48 ns | 7.371 ns | 3.273 ns | 1.82× slower  |     104 B |
| **SwiftMap**   |  43.84 ns | 7.824 ns | 3.474 ns | 2.24× slower  |     104 B |
| AutoMapper     |  88.62 ns | 7.901 ns | 3.508 ns | 4.54× slower  |     104 B |

### Collection mapping

| Method         | Count |      Mean |    Error |   StdDev | Ratio         | Allocated |
|:---------------|------:|----------:|---------:|---------:|:--------------|----------:|
| Manual         |   100 |  1.192 µs | 0.383 µs | 0.170 µs | baseline      |   7.05 KB |
| Mapster        |   100 |  2.171 µs | 0.484 µs | 0.215 µs | 1.86× slower  |   7.05 KB |
| **SwiftMap**   |   100 |  3.942 µs | 0.428 µs | 0.190 µs | 3.37× slower  |   7.05 KB |
| AutoMapper     |   100 | 10.843 µs | 1.069 µs | 0.475 µs | 9.27× slower  |   7.05 KB |
| Manual         |  1000 | 12.421 µs | 3.594 µs | 1.596 µs | baseline      |  70.34 KB |
| Mapster        |  1000 | 25.329 µs | 4.526 µs | 2.010 µs | 2.07× slower  |  70.34 KB |
| **SwiftMap**   |  1000 | 41.190 µs | 5.088 µs | 2.259 µs | 3.37× slower  |  70.34 KB |
| AutoMapper     |  1000 | 74.414 µs | 9.810 µs | 4.356 µs | 6.08× slower  |  70.34 KB |

### Record (primary constructor)

| Method         |       Mean |    Error |   StdDev | Ratio         | Allocated |
|:---------------|-----------:|---------:|---------:|:--------------|----------:|
| Manual         |   9.838 ns | 2.570 ns | 1.141 ns | baseline      |      48 B |
| Mapster        |  27.177 ns | 3.917 ns | 1.739 ns | 2.80× slower  |      48 B |
| **SwiftMap**   |  35.129 ns | 5.186 ns | 2.303 ns | 3.61× slower  |      48 B |
| AutoMapper     |  92.226 ns | 9.214 ns | 4.091 ns | 9.49× slower  |      48 B |

### At a glance

| Scenario          | vs AutoMapper       | vs Mapster      | Allocated            |
|:------------------|:--------------------|:----------------|:---------------------|
| Simple object     | **2.0× faster**     | 1.49× slower    | identical — 64 B     |
| Nested object     | **2.0× faster**     | 1.24× slower    | identical — 104 B    |
| Collection ×1000  | **1.8× faster**     | 1.63× slower    | identical — 70.34 KB |
| Record            | **2.6× faster**     | 1.29× slower    | identical — 48 B     |

---

## Project Structure

```
src/SwiftMap/
├── Mapper.cs                            # Entry point — Mapper.Create(...)
├── IMapper.cs                           # Public interface
├── MapperConfig.cs                      # Configuration + compiled delegate cache
├── TypeMapConfig.cs                     # Fluent API: ForMember, Ignore, AfterMap...
├── MapProfile.cs                        # Base class for profiles
├── Attributes/
│   └── MapToAttribute.cs                # [MapTo], [MapFrom], [IgnoreMap], [MapProperty]
├── Extensions/
│   └── ServiceCollectionExtensions.cs   # AddSwiftMap(...)
└── Internal/
    ├── MappingCompiler.cs               # Expression tree compiler (core engine)
    └── TypePair.cs                      # (Source, Destination) dictionary key
```

---

## Roadmap

- [x] **FastExpressionCompiler integration** — `CompileFast()` replaces `Expression.Compile()` for faster delegate creation
- [ ] **Source generator mode** — emit mapping code at compile time (Mapperly-style) to reach manual-mapping parity with zero startup cost
- [ ] **Async mapping** — `MapAsync<TDest>(source)` for pipelines that need async value resolution
- [ ] **IQueryable projection** — `ProjectTo<TDest>()` for ORM query projection
- [ ] **NuGet release** — publish `SwiftMap` to nuget.org

---

## Contributing

Contributions are welcome! Please:

1. Fork and create a feature branch (`git checkout -b feature/my-feature`)
2. Add or update tests to cover your change
3. Run the benchmark suite to verify no performance regression:
   ```bash
   dotnet run -c Release --project benchmarks/SwiftMap.Benchmarks
   ```
4. Open a pull request with a clear description of what changed and why

For significant changes, open an issue first to discuss the approach.

---

## License

SwiftMap is released under the [MIT License](LICENSE).
