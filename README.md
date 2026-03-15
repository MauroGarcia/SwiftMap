<div align="right"><a href="README.pt-BR.md">🇧🇷 Português</a></div>

<div align="center">

# SwiftMap

**Zero-allocation, expression-tree-compiled object mapper for .NET**

[![NuGet](https://img.shields.io/nuget/v/SwiftMap?style=flat-square&color=004880&label=NuGet)](https://www.nuget.org/packages/SwiftMap)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)

_Convention-based property mapping backed by compiled expression trees — zero reflection at call time._
_Source generator mode achieves **manual-mapping parity** at compile time (Mapperly-style)._

</div>

---

## Why SwiftMap?

- **Source generator mode** — `[Mapper]` partial class emits mapping bodies at **compile time**; zero startup cost, zero delegate dispatch, zero reflection at call time — identical to hand-written code
- **Runtime expression-tree mode** — mappings compile once to native delegates via [FastExpressionCompiler](https://github.com/dadhi/FastExpressionCompiler); subsequent calls are allocation-free
- **Fluent, discoverable API** — `ForMember`, `Ignore`, `AfterMap`, `ReverseMap`, `NullSubstitute`, `Condition`, `Patch`, and more
- **No heavy dependencies** — single NuGet package; only depends on `Microsoft.Extensions.DependencyInjection.Abstractions`
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

### Source Generator Mode (recommended — compile-time, zero overhead)

```csharp
using SwiftMap;

[Mapper]
public partial class AppMapper
{
    public partial PersonDto   Map(Person source);
    public partial OrderDto    Map(Order source);
    public partial ProductDto  Map(Product source);
}

// Usage — no DI, no startup cost, velocity = manual code
var mapper = new AppMapper();
var dto = mapper.Map(person);
```

The `[Mapper]` attribute triggers the included Roslyn source generator. It analyses your types at compile time and emits efficient object-initializer bodies — supporting flat objects, nested objects (null-safe is-pattern), collections (for-loop, no LINQ), records (primary constructor), enums, and nullable unwrapping.

### Runtime Expression-Tree Mode

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

### HTTP PATCH — Patch Semantics

`Patch` applies only the **non-null fields** from source onto an existing destination instance, leaving everything else untouched. Designed for HTTP PATCH endpoints where the client sends only the fields it wants to change.

```csharp
// Only Name was sent — Age and Email are null (not provided by client)
var updateDto = new UpdateUserDto { Name = "João", Age = null };
var user = await dbContext.Users.FindAsync(id); // { Name = "Maria", Age = 30, Email = "m@m.com" }

mapper.Patch(updateDto, user);
// Result: { Name = "João", Age = 30, Email = "m@m.com" }
// Age and Email were preserved — they were null in the dto
```

Works automatically by convention — no `CreateMap` required. For advanced scenarios:

```csharp
// Skip fields that match their default value (0, false, Guid.Empty, etc.)
mapper.Patch(dto, entity, cfg => cfg.AsPatch(PatchBehavior.SkipDefaultFields));

// Register patch behavior in a profile
public class AppProfile : MapProfile
{
    public AppProfile()
    {
        CreateMap<UpdateUserDto, User>().AsPatch();
    }
}
```

| Source field type | Behavior |
|:------------------|:---------|
| `string` / reference type | Skipped if `null` |
| `Nullable<T>` (`int?`, `bool?`, …) | Skipped if `!HasValue` |
| Non-nullable value type (`int`, `bool`, …) | Always applied (cannot be null) |

---

## Benchmarks

Benchmarked against **AutoMapper 13.0.1** and **Mapster 7.4.0** on .NET 9.

```
BenchmarkDotNet v0.15.8  ·  Windows 11  ·  AMD Ryzen 5 3600 3.60GHz (6C/12T)
.NET SDK 9.0.312  ·  .NET 9.0.14  ·  X64 RyuJIT x86-64-v3
ShortRun: 3 warmups + 7 iterations
```

### Simple flat object (7 properties)

| Method              |      Mean |    Error |   StdDev | Ratio         | Allocated |
|:--------------------|----------:|---------:|---------:|:--------------|----------:|
| Manual              |  15.19 ns | 4.863 ns | 2.159 ns | baseline      |      64 B |
| **SwiftGenerated**  |  15.27 ns | 5.110 ns | 2.269 ns | **1.03×**     |      64 B |
| Mapster             |  30.37 ns | 4.921 ns | 2.185 ns | 2.04× slower  |      64 B |
| SwiftMap (runtime)  |  39.75 ns | 8.234 ns | 3.656 ns | 2.67× slower  |      64 B |
| AutoMapper          |  89.95 ns |17.732 ns | 7.873 ns | 6.04× slower  |      64 B |

### Nested object (parent + child)

| Method              |      Mean |    Error |   StdDev | Ratio         | Allocated |
|:--------------------|----------:|---------:|---------:|:--------------|----------:|
| Manual              |  22.18 ns | 6.680 ns | 2.966 ns | baseline      |     104 B |
| **SwiftGenerated**  |  22.97 ns | 6.421 ns | 2.851 ns | **1.05×**     |     104 B |
| Mapster             |  40.52 ns | 8.772 ns | 3.895 ns | 1.86× slower  |     104 B |
| SwiftMap (runtime)  |  48.76 ns |13.197 ns | 5.859 ns | 2.23× slower  |     104 B |
| AutoMapper          | 102.62 ns |18.835 ns | 8.363 ns | 4.70× slower  |     104 B |

### Collection mapping

| Method              | Count |      Mean |     Error |    StdDev | Ratio         | Allocated |
|:--------------------|------:|----------:|----------:|----------:|:--------------|----------:|
| Manual              |   100 |  1.425 µs |  0.534 µs |  0.237 µs | baseline      |   7.05 KB |
| **SwiftGenerated**  |   100 |  1.519 µs |  0.573 µs |  0.254 µs | **1.09×**     |   7.05 KB |
| Mapster             |   100 |  2.637 µs |  0.542 µs |  0.241 µs | 1.90× slower  |   7.05 KB |
| SwiftMap (runtime)  |   100 |  4.372 µs |  0.968 µs |  0.430 µs | 3.14× slower  |   7.05 KB |
| AutoMapper          |   100 |  8.486 µs |  3.128 µs |  1.389 µs | 6.10× slower  |   7.05 KB |
| Manual              |  1000 | 14.202 µs |  5.038 µs |  2.237 µs | baseline      |  70.34 KB |
| **SwiftGenerated**  |  1000 | 15.540 µs |  4.086 µs |  1.814 µs | **1.12×**     |  70.34 KB |
| Mapster             |  1000 | 30.888 µs |  7.347 µs |  3.262 µs | 2.23× slower  |  70.34 KB |
| SwiftMap (runtime)  |  1000 | 43.908 µs | 13.642 µs |  6.057 µs | 3.17× slower  |  70.34 KB |
| AutoMapper          |  1000 | 91.405 µs | 14.038 µs |  6.233 µs | 6.59× slower  |  70.34 KB |

### Record (primary constructor)

| Method              |      Mean |    Error |   StdDev | Ratio         | Allocated |
|:--------------------|----------:|---------:|---------:|:--------------|----------:|
| Manual              |  11.27 ns | 4.766 ns | 2.116 ns | baseline      |      48 B |
| **SwiftGenerated**  |  12.89 ns | 4.260 ns | 1.891 ns | **1.18×**     |      48 B |
| Mapster             |  30.10 ns | 6.656 ns | 2.955 ns | 2.74× slower  |      48 B |
| SwiftMap (runtime)  |  37.35 ns | 7.100 ns | 3.153 ns | 3.40× slower  |      48 B |
| AutoMapper          |  86.29 ns |16.937 ns | 7.520 ns | 7.86× slower  |      48 B |

### At a glance

| Scenario          | SwiftGenerated vs Manual | SwiftGenerated vs Mapster | SwiftGenerated vs AutoMapper |
|:------------------|:------------------------:|:-------------------------:|:----------------------------:|
| Simple object     | **≈ parity (1.03×)**     | **2.0× faster**           | **5.9× faster**              |
| Nested object     | **≈ parity (1.05×)**     | **1.8× faster**           | **4.5× faster**              |
| Collection ×1000  | **≈ parity (1.12×)**     | **2.0× faster**           | **5.9× faster**              |
| Record            | **≈ parity (1.18×)**     | **2.3× faster**           | **6.7× faster**              |

> All measurements: same allocated memory as manual code (no overhead).

---

## Project Structure

```
src/
├── SwiftMap/                              # Runtime expression-tree mapper
│   ├── Mapper.cs                          # Entry point — Mapper.Create(...)
│   ├── IMapper.cs                         # Public interface
│   ├── MapperConfig.cs                    # Configuration + compiled delegate cache
│   ├── TypeMapConfig.cs                   # Fluent API: ForMember, Ignore, AfterMap...
│   ├── MapProfile.cs                      # Base class for profiles
│   ├── Attributes/
│   │   └── MapToAttribute.cs              # [MapTo], [MapFrom], [IgnoreMap], [MapProperty], [Mapper]
│   ├── Extensions/
│   │   └── ServiceCollectionExtensions.cs # AddSwiftMap(...)
│   └── Internal/
│       ├── MappingCompiler.cs             # Expression tree compiler (core engine)
│       └── TypePair.cs                    # (Source, Destination) dictionary key
└── SwiftMap.SourceGenerator/              # Roslyn IIncrementalGenerator
    ├── MapperGenerator.cs                 # [Generator] entry point
    ├── Models/                            # MapperClassModel, MappingMethodModel, MappedPropertyModel
    ├── Pipeline/
    │   └── MapperModelExtractor.cs        # Semantic analysis
    └── Emit/
        ├── MappingBodyEmitter.cs          # Code generation
        └── SourceWriter.cs                # Indented string builder
```

---

## Roadmap

- [x] **FastExpressionCompiler integration** — `CompileFast()` replaces `Expression.Compile()` for faster delegate creation
- [x] **Source generator mode** — `[Mapper]` partial class emits mapping bodies at compile time; reaches manual-mapping parity
- [x] **Patch semantics** — `mapper.Patch(dto, entity)` applies only non-null fields; built-in support for HTTP PATCH endpoints
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
