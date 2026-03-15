# SwiftMap

A convention-based object-object mapper for .NET 9, backed by compiled expression trees — zero reflection at mapping time.

## Features

- **Convention mapping** — matches properties by name (case-insensitive) automatically
- **Flattening** — maps `Address.City` → `AddressCity` with null safety
- **Record support** — maps to/from records via primary constructor
- **Init-only properties** — supported via constructor selection
- **Struct mapping** — value types handled correctly
- **Collection mapping** — `List<T>`, arrays, and other `IEnumerable<T>`
- **Enum conversions** — enum↔enum, enum↔string, string→enum
- **Nullable handling** — `int?` → `int` and vice versa
- **Nested objects** — null-safe recursive mapping
- **Fluent API** — `ForMember`, `Ignore`, `ConstructUsing`, `AfterMap`, `ReverseMap`, `NullSubstitute`, `Condition`, `MapFrom`
- **Attribute mapping** — `[MapTo]`, `[MapFrom]`, `[IgnoreMap]`, `[MapProperty]`
- **Profiles** — organize mappings via `MapProfile` base class
- **DI integration** — `services.AddSwiftMap(...)`
- **Thread-safe** — compiled delegates cached in `ConcurrentDictionary`

## Installation

```bash
dotnet add package SwiftMap
```

## Quick Start

```csharp
var mapper = Mapper.Create(cfg =>
    cfg.CreateMap<PersonSource, PersonDest>());

var dest = mapper.Map<PersonDest>(source);
```

## Usage

### Convention mapping (no config needed)

```csharp
var mapper = Mapper.Create(_ => { });
var dto = mapper.Map<CustomerDto>(customer);
```

### Fluent configuration

```csharp
var mapper = Mapper.Create(cfg =>
    cfg.CreateMap<Customer, CustomerDto>(map =>
        map.ForMember(d => d.AddressCity, opt => opt.MapFrom(s => s.Address!.City))
           .Ignore(d => d.Email)
           .AfterMap((src, dest) => dest.Name = dest.Name.ToUpperInvariant())));
```

### Profiles

```csharp
public class AppProfile : MapProfile
{
    public AppProfile()
    {
        CreateMap<Customer, CustomerDto>()
            .ForMember(d => d.AddressCity, opt => opt.MapFrom(s => s.Address!.City));
    }
}

var mapper = Mapper.Create(cfg => cfg.AddProfile<AppProfile>());
```

### Attributes

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
// Inline config
services.AddSwiftMap(cfg => cfg.CreateMap<Order, OrderDto>());

// Scan assemblies for profiles and [MapTo]/[MapFrom] attributes
services.AddSwiftMap(typeof(Program).Assembly);
```

### Map into existing instance

```csharp
mapper.Map(source, existingDestination);
```

---

## Benchmarks

Benchmarked against **AutoMapper 13.0.1** and **Mapster 7.4.0** on .NET 9.

**Environment:**
```
BenchmarkDotNet v0.15.8 · Windows 11 · AMD Ryzen 5 3600 3.60GHz (6C/12T)
.NET SDK 9.0.312 · .NET 9.0.14 · X64 RyuJIT x86-64-v3
ShortRun: 3 warmups + 7 iterations
```

### Simple flat object (7 properties)

| Method         | Mean      | Ratio        | Allocated |
|----------------|----------:|-------------:|----------:|
| Manual         |  11.37 ns | baseline     |      64 B |
| Mapster        |  24.08 ns | 2.14x slower |      64 B |
| **SwiftMap**   |  34.06 ns | 3.02x slower |      64 B |
| AutoMapper     |  74.40 ns | 6.60x slower |      64 B |

### Nested object (parent + child)

| Method         | Mean      | Ratio        | Allocated |
|----------------|----------:|-------------:|----------:|
| Manual         |  19.13 ns | baseline     |     104 B |
| Mapster        |  35.79 ns | 1.88x slower |     104 B |
| **SwiftMap**   |  44.87 ns | 2.35x slower |     104 B |
| AutoMapper     |  86.48 ns | 4.54x slower |     104 B |

### Collection (N items)

| Method         | Count | Mean      | Ratio        | Allocated |
|----------------|------:|----------:|-------------:|----------:|
| Manual         |   100 |  1.026 µs | baseline     |   7.05 KB |
| Mapster        |   100 |  2.149 µs | 2.09x slower |   7.05 KB |
| **SwiftMap**   |   100 |  3.874 µs | 3.78x slower |   7.05 KB |
| AutoMapper     |   100 |  7.367 µs | 7.18x slower |   7.05 KB |
| Manual         |  1000 | 12.423 µs | baseline     |  70.34 KB |
| Mapster        |  1000 | 23.752 µs | 1.94x slower |  70.34 KB |
| **SwiftMap**   |  1000 | 39.950 µs | 3.26x slower |  70.34 KB |
| AutoMapper     |  1000 | 77.463 µs | 6.33x slower |  70.34 KB |

### Record (primary constructor)

| Method         | Mean      | Ratio        | Allocated |
|----------------|----------:|-------------:|----------:|
| Manual         |  8.636 ns | baseline     |      48 B |
| Mapster        | 24.900 ns | 2.89x slower |      48 B |
| **SwiftMap**   | 36.073 ns | 4.19x slower |      48 B |
| AutoMapper     | 76.145 ns | 8.84x slower |      48 B |

### Analysis

| Scenario         | SwiftMap vs AutoMapper  | SwiftMap vs Mapster  | Allocated        |
|------------------|-------------------------|----------------------|------------------|
| Simple object    | **2.2x faster**         | 1.4x slower          | identical (64 B) |
| Nested object    | **1.9x faster**         | 1.3x slower          | identical (104 B)|
| Collection ×1000 | **1.9x faster**         | 1.7x slower          | identical (70 KB)|
| Record           | **2.1x faster**         | 1.5x slower          | identical (48 B) |

SwiftMap allocates exactly the same as AutoMapper and Mapster across all scenarios — only the destination objects. Nested mappings are inlined at compile time into the parent expression tree, and collections use pre-allocated for-loops instead of LINQ.

Mapster is fastest due to Roslyn source generation (compile-time code emission vs. runtime expression tree compilation).

---

## Project Structure

```
src/SwiftMap/
├── Mapper.cs                        # Entry point: Mapper.Create(...)
├── IMapper.cs                       # Public interface
├── MapperConfig.cs                  # Configuration + compiled delegate cache
├── TypeMapConfig.cs                 # Fluent API: ForMember, Ignore, AfterMap...
├── MapProfile.cs                    # Base class for profiles
├── Attributes/
│   └── MapToAttribute.cs            # [MapTo], [MapFrom], [IgnoreMap], [MapProperty]
├── Extensions/
│   └── ServiceCollectionExtensions.cs  # AddSwiftMap(...)
└── Internal/
    ├── MappingCompiler.cs           # Expression tree compiler (core engine)
    └── TypePair.cs                  # (Source, Destination) dictionary key
```

## License

MIT
