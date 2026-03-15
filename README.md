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
| Manual         |  12.15 ns | baseline     |      64 B |
| Mapster        |  28.19 ns | 2.35x slower |      64 B |
| **SwiftMap**   |  76.99 ns | 6.43x slower |     192 B |
| AutoMapper     |  77.87 ns | 6.51x slower |      64 B |

### Nested object (parent + child)

| Method         | Mean       | Ratio        | Allocated |
|----------------|-----------:|-------------:|----------:|
| Manual         |   19.66 ns | baseline     |     104 B |
| Mapster        |   38.44 ns | 1.96x slower |     104 B |
| AutoMapper     |   86.66 ns | 4.42x slower |     104 B |
| **SwiftMap**   |  178.36 ns | 9.09x slower |     360 B |

### Collection (N items)

| Method         | Count | Mean      | Ratio        | Allocated  |
|----------------|------:|----------:|-------------:|-----------:|
| Manual         |   100 |  1.282 µs | baseline     |    7.05 KB |
| Mapster        |   100 |  2.094 µs | 1.66x slower |    7.05 KB |
| **SwiftMap**   |   100 |  7.110 µs | 5.65x slower |   19.55 KB |
| AutoMapper     |   100 |  7.719 µs | 6.13x slower |    7.05 KB |
| Manual         |  1000 | 11.958 µs | baseline     |   70.34 KB |
| Mapster        |  1000 | 24.255 µs | 2.04x slower |   70.34 KB |
| **SwiftMap**   |  1000 | 70.002 µs | 5.88x slower |  195.34 KB |
| AutoMapper     |  1000 | 70.880 µs | 5.95x slower |   70.34 KB |

### Record (primary constructor)

| Method         | Mean      | Ratio        | Allocated |
|----------------|----------:|-------------:|----------:|
| Manual         |   9.928 ns | baseline    |      48 B |
| Mapster        |  26.446 ns | 2.67x slower |      48 B |
| **SwiftMap**   |  71.047 ns | 7.16x slower |     176 B |
| AutoMapper     |  81.177 ns | 8.19x slower |      48 B |

### Analysis

| Scenario       | SwiftMap vs AutoMapper      |
|----------------|-----------------------------|
| Simple object  | ~tied (76.99 vs 77.87 ns)   |
| Nested object  | 2.1x slower                 |
| Collection ×1000 | ~tied (70.0 vs 70.9 µs)  |
| Record         | **12% faster**              |

SwiftMap matches or beats AutoMapper in flat and collection scenarios. The allocation overhead (~3x) comes from `MapperContext` passing and boxing through nested map calls — the primary optimization target.

Mapster is fastest across all scenarios due to Roslyn source generation (compile-time code emission).

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
    ├── MapperContext.cs             # Runtime context for nested mapping
    └── TypePair.cs                  # (Source, Destination) dictionary key
```

## License

MIT
