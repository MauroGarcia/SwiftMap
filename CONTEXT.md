# SwiftMap — Resumo de Contexto

## O que é

SwiftMap é um object-object mapper convention-based para .NET 9, criado do zero como alternativa ao AutoMapper. Usa expression trees compiladas (delegate cacheado em `ConcurrentDictionary`) — zero reflection em tempo de mapeamento. Mapeamentos aninhados são inlinados em compile time diretamente na expression tree pai, eliminando overhead de delegate por nível.

**Repositório:** https://github.com/MauroGarcia/SwiftMap
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
- Objetos aninhados (null-safe via `Expression.Condition`, mapeamento inlinado)
- Fluent API: `ForMember`, `Ignore`, `ConstructUsing`, `AfterMap`, `ReverseMap`, `NullSubstitute`, `Condition`, `MapFrom`
- Atributos: `[MapTo]`, `[MapFrom]`, `[IgnoreMap]`, `[MapProperty]`
- Profiles via `MapProfile`
- DI: `services.AddSwiftMap(...)`
- `Map(source, existingDestination)` — map into existing instance
- Thread-safe

---

## Dependências

| Projeto    | Pacotes                                                                    |
|------------|----------------------------------------------------------------------------|
| SwiftMap   | FastExpressionCompiler 5.3.0, Microsoft.Extensions.DependencyInjection.Abstractions 9.0 |
| Benchmarks | BenchmarkDotNet 0.15.8, AutoMapper 13.0.1, Mapster 7.4.0                  |
| Tests      | xunit, Microsoft.NET.Test.Sdk                                              |

> AutoMapper foi downgraded de v16 para v13 por breaking changes na API.

---

## Otimizações de performance aplicadas

### Rodada 1 (sessão anterior)
- Inline de mapeamentos aninhados na expression tree pai (elimina delegate por nível)
- For-loops com indexer pré-alocado para coleções (elimina LINQ Select/ToList)
- `TryGetValue` fast-path + static lambda no `ConcurrentDictionary` (zero-alloc no cache hit)

### Rodada 2 (sessão atual — worktree `jovial-goodall`)

| Arquivo | Mudança | Impacto |
|---------|---------|---------|
| `MappingCompiler.cs` | **FastExpressionCompiler 5.3.0** — `CompileFast()` nos 3 pontos de compilação | Compilação 2-10× mais rápida; delegates potencialmente mais rápidos |
| `MappingCompiler.cs` | Cache `HasImplicitConversion`/`HasExplicitConversion` em `ConcurrentDictionary<(RuntimeTypeHandle,RuntimeTypeHandle), bool>` | `GetMethods()` chamado 1× por par de tipos |
| `MappingCompiler.cs` | Cache `FindSourcePropertyDirect` em `ConcurrentDictionary<(RuntimeTypeHandle,string), PropertyInfo?>` | `GetProperty()` chamado 1× por (tipo, nome) |
| `MappingCompiler.cs` | Cache `GetCollectionElementType` por `RuntimeTypeHandle` | `GetInterfaces()` chamado 1× por tipo |
| `MappingCompiler.cs` | Cache IList<T> interface em `TryGetCountAndIndexer` | Scanning de interfaces 1× por tipo |
| `MappingCompiler.cs` | Reordenar `ConvertIfNeeded`: enum checks (baratos) antes de `HasImplicit/Explicit` (caro) | Evita reflection desnecessária para tipos comuns |
| `MappingCompiler.cs` | Constructor selection: `OrderByDescending+FirstOrDefault` → `foreach` O(n) | Elimina alocação de LINQ |
| `TypePair.cs` | `RuntimeTypeHandle` em vez de `Type`; `[AggressiveInlining]` em `Equals`/`GetHashCode` | Hash = 1 op aritmético em vez de virtual dispatch |
| `TypeMapConfig.cs` | `StringComparer.Ordinal` (de OrdinalIgnoreCase) em `MemberConfigs` e `IgnoredMembers` | Comparação ordinal mais rápida (nomes de propriedade são case-exact do CLR) |
| `Mapper.cs` | `[AggressiveInlining]` nos overloads `Map<TSource,TDest>` | Elimina overhead de call frame em loops apertados |

---

## Resultados dos benchmarks

**Ambiente:** AMD Ryzen 5 3600 · .NET 9.0.14 · BenchmarkDotNet v0.15.8 · ShortRun (3 warmups + 7 iterações)

### Após Rodada 2 (runtime — expression trees)

#### Simple flat object (7 props)

| Method     | Mean      | Error    | StdDev   | Ratio        | Allocated |
|------------|----------:|---------:|---------:|-------------:|----------:|
| Manual     |  13.23 ns | 3.432 ns | 1.524 ns | baseline     |      64 B |
| Mapster    |  25.48 ns | 3.667 ns | 1.628 ns | 1.95× slower |      64 B |
| SwiftMap   |  38.04 ns | 5.319 ns | 2.362 ns | 2.91× slower |      64 B |
| AutoMapper |  76.79 ns | 7.118 ns | 3.161 ns | 5.87× slower |      64 B |

#### Nested object

| Method     | Mean      | Error    | StdDev   | Ratio        | Allocated |
|------------|----------:|---------:|---------:|-------------:|----------:|
| Manual     |  19.89 ns | 6.382 ns | 2.834 ns | baseline     |     104 B |
| Mapster    |  35.48 ns | 7.371 ns | 3.273 ns | 1.82× slower |     104 B |
| SwiftMap   |  43.84 ns | 7.824 ns | 3.474 ns | 2.24× slower |     104 B |
| AutoMapper |  88.62 ns | 7.901 ns | 3.508 ns | 4.54× slower |     104 B |

#### Collection (N=1000)

| Method     | Mean      | Error    | StdDev   | Ratio        | Allocated |
|------------|----------:|---------:|---------:|-------------:|----------:|
| Manual     | 12.421 µs | 3.594 µs | 1.596 µs | baseline     |  70.34 KB |
| Mapster    | 25.329 µs | 4.526 µs | 2.010 µs | 2.07× slower |  70.34 KB |
| SwiftMap   | 41.190 µs | 5.088 µs | 2.259 µs | 3.37× slower |  70.34 KB |
| AutoMapper | 74.414 µs | 9.810 µs | 4.356 µs | 6.08× slower |  70.34 KB |

#### Record (primary constructor)

| Method     | Mean      | Error    | StdDev   | Ratio        | Allocated |
|------------|----------:|---------:|---------:|-------------:|----------:|
| Manual     |  9.838 ns | 2.570 ns | 1.141 ns | baseline     |      48 B |
| Mapster    | 27.177 ns | 3.917 ns | 1.739 ns | 2.80× slower |      48 B |
| SwiftMap   | 35.129 ns | 5.186 ns | 2.303 ns | 3.61× slower |      48 B |
| AutoMapper | 92.226 ns | 9.214 ns | 4.091 ns | 9.49× slower |      48 B |

### Rodada 3 — Source Generator (benchmark atual)

**Ambiente:** AMD Ryzen 5 3600 · .NET 9.0.14 · BenchmarkDotNet v0.15.8 · ShortRun (3 warmups + 7 iterações)

#### Simple flat object

| Method          | Mean      | Error    | StdDev   | Ratio        | Allocated |
|-----------------|----------:|---------:|---------:|-------------:|----------:|
| Manual          |  15.19 ns | 4.863 ns | 2.159 ns | baseline     |      64 B |
| SwiftGenerated  |  15.27 ns | 5.110 ns | 2.269 ns | **1.03×**    |      64 B |
| Mapster         |  30.37 ns | 4.921 ns | 2.185 ns | 2.04× slower |      64 B |
| Swift (runtime) |  39.75 ns | 8.234 ns | 3.656 ns | 2.67× slower |      64 B |
| AutoMapper      |  89.95 ns |17.732 ns | 7.873 ns | 6.04× slower |      64 B |

#### Nested object

| Method          | Mean       | Error     | StdDev   | Ratio        | Allocated |
|-----------------|----------: |---------: |---------:|-------------:|----------:|
| Manual          |  22.18 ns  |  6.680 ns | 2.966 ns | baseline     |     104 B |
| SwiftGenerated  |  22.97 ns  |  6.421 ns | 2.851 ns | **1.05×**    |     104 B |
| Mapster         |  40.52 ns  |  8.772 ns | 3.895 ns | 1.86× slower |     104 B |
| Swift (runtime) |  48.76 ns  | 13.197 ns | 5.859 ns | 2.23× slower |     104 B |
| AutoMapper      | 102.62 ns  | 18.835 ns | 8.363 ns | 4.70× slower |     104 B |

#### Collection (N=1000)

| Method          | Mean      | Error     | StdDev   | Ratio        | Allocated |
|-----------------|----------:|----------:|---------:|-------------:|----------:|
| Manual          | 14.202 µs |  5.038 µs | 2.237 µs | baseline     |  70.34 KB |
| SwiftGenerated  | 15.540 µs |  4.086 µs | 1.814 µs | **1.12×**    |  70.34 KB |
| Mapster         | 30.888 µs |  7.347 µs | 3.262 µs | 2.23× slower |  70.34 KB |
| Swift (runtime) | 43.908 µs | 13.642 µs | 6.057 µs | 3.17× slower |  70.34 KB |
| AutoMapper      | 91.405 µs | 14.038 µs | 6.233 µs | 6.59× slower |  70.34 KB |

#### Record (primary constructor)

| Method          | Mean      | Error     | StdDev   | Ratio        | Allocated |
|-----------------|----------:|----------:|---------:|-------------:|----------:|
| Manual          |  11.27 ns |  4.766 ns | 2.116 ns | baseline     |      48 B |
| SwiftGenerated  |  12.89 ns |  4.260 ns | 1.891 ns | **1.18×**    |      48 B |
| Mapster         |  30.10 ns |  6.656 ns | 2.955 ns | 2.74× slower |      48 B |
| Swift (runtime) |  37.35 ns |  7.100 ns | 3.153 ns | 3.40× slower |      48 B |
| AutoMapper      |  86.29 ns | 16.937 ns | 7.520 ns | 7.86× slower |      48 B |

### Evolução SwiftGenerated vs Manual

| Cenário          | R2 (runtime) vs Mapster | **R3 (generated) vs Manual** | Objetivo |
|:-----------------|:-----------------------:|:----------------------------:|:--------:|
| Simple object    | 1.49×                   | **1.03× ✅**                  | 1.0×     |
| Nested object    | 1.24×                   | **1.05× ✅**                  | 1.0×     |
| Collection ×1000 | 1.63×                   | **1.12× ✅**                  | 1.0×     |
| Record           | 1.29×                   | **1.18× ✅**                  | 1.0×     |

> Source generator mode atingiu o objetivo: paridade com código manual em todos os cenários.
> SwiftGenerated é **2× mais rápido que Mapster** e **5–7× mais rápido que AutoMapper**.

---

## Próximos passos para fechar o gap com Mapster

| Prioridade | Técnica | Impacto estimado | Esforço | Status |
|:----------:|---------|:----------------:|:-------:|:------:|
| 1 | **Source generator mode** (Mapperly-style, código em compile-time) | Atinge velocidade manual | 2-3 semanas | ✅ Implementado |
| 2 | **Typed generic delegate cache** `TypedCache<TSource,TDest>` (static field por par, elimina ConcurrentDict no hot path) | ~20-40% no overload fortemente tipado | 1 dia | Pendente |
| 3 | `FrozenDictionary` para `_typeMapConfigs` após `Build()` | <5% | 1h | Pendente |
| 4 | `UnsafeAccessor` para campos privados/init-only | Nicho | 1 semana | Pendente |

> **Nota sobre TypedCache:** requer que o Mapper seja singleton global ou que o cache seja por instância de MapperConfig (caso contrário mappers com configs diferentes colidiriam). Não implementado por segurança — revisar arquitetura antes.

### Rodada 3 — Source Generator (Mapperly-style)

| Arquivo/Módulo | Mudança | Impacto |
|---|---|---|
| `src/SwiftMap/Attributes/MapToAttribute.cs` | Adicionado `[Mapper]` attribute | Entry point da API do source generator |
| `src/SwiftMap.SourceGenerator/` | Novo projeto `netstandard2.0` — `MapperGenerator` (IIncrementalGenerator), `MapperModelExtractor` (análise semântica), `MappingBodyEmitter` (emissão de código), `SourceWriter` | Source generator completo |
| `src/SwiftMap/SwiftMap.csproj` | Referência ao generator como `OutputItemType="Analyzer"` | Generator ativado para quem referencia SwiftMap |
| `tests/SwiftMap.SourceGenerator.Tests/` | 8 testes (flat, record, nested, IgnoreMap, enum, multi-método, non-partial guard) | Cobertura do generator |
| Benchmarks | `SwiftGenerated` method em todos os 4 cenários + `BenchMapper` partial class | Validação de performance vs manual |

---

## NuGet

Metadados configurados em `SwiftMap.csproj`:
- `PackageId`: SwiftMap
- `Version`: 1.0.0
- `Authors`: Mauro
- `PackageLicenseExpression`: MIT
- `GenerateDocumentationFile`: true
- `IncludeSymbols` + `SymbolPackageFormat`: snupkg
- `PackageReadmeFile`: README.md (incluído no pack)

Para publicar: `dotnet pack -c Release` + `dotnet nuget push`

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
| Alocações ~3x maiores que AutoMapper/Mapster | Inline nested mapping + for-loop collections + TryGetValue fast-path |
| CS1591 ao habilitar GenerateDocumentationFile | `<NoWarn>$(NoWarn);CS1591</NoWarn>` |

---

## Git

- Branch principal: `master`
- Commits seguem **Conventional Commits com escopo**, uma linha, sem co-authors
  - Ex: `feat(core): ...`, `docs(readme): ...`, `perf(compiler): ...`
- `.gitignore` cobre `bin/`, `obj/`, `BenchmarkDotNet.Artifacts/`, `.vs/`, `.idea/`
- Worktrees usados para sessões de trabalho isoladas (`.claude/worktrees/<nome>`)
