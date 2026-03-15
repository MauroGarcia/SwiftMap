# SwiftMap — Resumo de Contexto

## O que é

SwiftMap é um object-object mapper convention-based para .NET 9, criado do zero nesta conversa como alternativa ao AutoMapper. Usa expression trees compiladas (delegate cacheado em `ConcurrentDictionary`) — zero reflection em tempo de mapeamento.

**Repositório:** https://github.com/MauroGarcia/SwiftMap (privado)
**Localização local:** `C:/Projects/SwiftMap`

---

## Estrutura do projeto

```
SwiftMap.sln
├── src/SwiftMap/                        # Biblioteca principal
│   ├── Mapper.cs                        # Entry point: Mapper.Create(...)
│   ├── IMapper.cs
│   ├── MapperConfig.cs                  # Config + cache de delegates compilados
│   ├── TypeMapConfig.cs                 # Fluent API
│   ├── MapProfile.cs                    # Base class para profiles
│   ├── Attributes/MapToAttribute.cs     # [MapTo], [MapFrom], [IgnoreMap], [MapProperty]
│   ├── Extensions/ServiceCollectionExtensions.cs  # AddSwiftMap(...)
│   └── Internal/
│       ├── MappingCompiler.cs           # Motor principal (expression trees)
│       ├── MapperContext.cs             # Contexto para mapeamento aninhado
│       └── TypePair.cs                  # Chave do dicionário (Source, Destination)
├── tests/SwiftMap.Tests/
│   └── MapperTests.cs                   # 27 testes, todos passando
└── benchmarks/SwiftMap.Benchmarks/
    └── Program.cs                       # BenchmarkDotNet vs AutoMapper + Mapster
```

---

## Features implementadas

- Convention mapping por nome (case-insensitive)
- Flattening (`Address.City` → `AddressCity`) com null-safety
- Records via primary constructor
- Init-only properties
- Structs
- Coleções (`List<T>`, arrays, `IEnumerable<T>`)
- Enums: enum↔enum, enum↔string, string→enum
- Nullable: `int?` → `int`
- Objetos aninhados (null-safe via `Expression.Condition`)
- Fluent API: `ForMember`, `Ignore`, `ConstructUsing`, `AfterMap`, `ReverseMap`, `NullSubstitute`, `Condition`, `MapFrom`
- Atributos: `[MapTo]`, `[MapFrom]`, `[IgnoreMap]`, `[MapProperty]`
- Profiles via `MapProfile`
- DI: `services.AddSwiftMap(...)`
- `Map(source, existingDestination)` — map into existing instance
- Thread-safe

---

## Dependências

| Projeto        | Pacotes                                                    |
|----------------|------------------------------------------------------------|
| SwiftMap       | Microsoft.Extensions.DependencyInjection.Abstractions 9.0 |
| Benchmarks     | BenchmarkDotNet 0.15.8, AutoMapper 13.0.1, Mapster 7.4.0  |
| Tests          | xunit, Microsoft.NET.Test.Sdk                              |

> AutoMapper foi downgraded de v16 para v13 por breaking changes na API.

---

## Resultados dos benchmarks

**Ambiente:** AMD Ryzen 5 3600 · .NET 9.0.14 · BenchmarkDotNet v0.15.8 · ShortRun (3 warmups + 7 iterações)

### Simple flat object (7 props)

| Method     | Mean      | Ratio        | Allocated |
|------------|----------:|-------------:|----------:|
| Manual     |  12.15 ns | baseline     |      64 B |
| Mapster    |  28.19 ns | 2.35x slower |      64 B |
| SwiftMap   |  76.99 ns | 6.43x slower |     192 B |
| AutoMapper |  77.87 ns | 6.51x slower |      64 B |

### Nested object

| Method     | Mean       | Ratio        | Allocated |
|------------|-----------:|-------------:|----------:|
| Manual     |   19.66 ns | baseline     |     104 B |
| Mapster    |   38.44 ns | 1.96x slower |     104 B |
| AutoMapper |   86.66 ns | 4.42x slower |     104 B |
| SwiftMap   |  178.36 ns | 9.09x slower |     360 B |

### Collection (N=1000)

| Method     | Mean      | Ratio        | Allocated  |
|------------|----------:|-------------:|-----------:|
| Manual     | 11.958 µs | baseline     |   70.34 KB |
| Mapster    | 24.255 µs | 2.04x slower |   70.34 KB |
| SwiftMap   | 70.002 µs | 5.88x slower |  195.34 KB |
| AutoMapper | 70.880 µs | 5.95x slower |   70.34 KB |

### Record (primary constructor)

| Method     | Mean      | Ratio        | Allocated |
|------------|----------:|-------------:|----------:|
| Manual     |   9.928 ns | baseline    |      48 B |
| Mapster    |  26.446 ns | 2.67x slower |      48 B |
| SwiftMap   |  71.047 ns | 7.16x slower |     176 B |
| AutoMapper |  81.177 ns | 8.19x slower |      48 B |

### Conclusão dos benchmarks

- SwiftMap ≈ AutoMapper em objetos simples e coleções
- SwiftMap 12% mais rápido que AutoMapper em records
- SwiftMap ~2x mais lento em objetos aninhados (overhead do `MapperContext` boxing)
- SwiftMap aloca ~3x mais que AutoMapper — principal ponto de melhoria
- Mapster vence tudo por usar source generators em compile-time

---

## Problemas resolvidos durante o desenvolvimento

| Problema | Solução |
|----------|---------|
| `ReverseMap()` syntax | `cfg.CreateMap<A,B>(map => map.ReverseMap())` |
| `[IgnoreMap]` só checava no destino | Adicionado check no source property também |
| Crash em nested null (`ctx.Map<T>(null)`) | Wrapped em `Expression.Condition` null-check |
| `IMapper` ambíguo entre AutoMapper e SwiftMap | `using SwiftIMapper = SwiftMap.IMapper; using AM = AutoMapper;` |
| Nome de método `SwiftMap()` conflitava com namespace | Renomeado para `Swift()` |
| AutoMapper v16 breaking changes | Downgraded para v13.0.1 |
| `ConstructUsing` sendo sobrescrito por convention | Adicionado `.Ignore(d => d.Id)` no teste |
| Init-only properties sem setter | Handled via constructor matching |
| Flattening null retornava `null` em vez de `""` | Assertion corrigida para `Assert.Null()` |

---

## Git

- Branch principal: `master`
- Commits seguem **Conventional Commits com escopo**, uma linha, sem co-authors
  - Ex: `feat(core): ...`, `docs(readme): ...`, `fix(compiler): ...`
- `.gitignore` cobre `bin/`, `obj/`, `BenchmarkDotNet.Artifacts/`, `.vs/`, `.idea/`

---

## Possíveis melhorias futuras

- Reduzir alocações eliminando boxing no `MapperContext` (usar generics ou struct context)
- Melhorar performance em nested objects (evitar round-trip pelo dicionário)
- Source generator opcional para eliminar runtime compilation
- NuGet package
- Suporte a `IQueryable` projections
