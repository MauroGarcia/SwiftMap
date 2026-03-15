<div align="right"><a href="README.md">🇺🇸 English</a></div>

<div align="center">

# SwiftMap

**Mapeador de objetos .NET sem alocações, compilado com expression trees**

[![NuGet](https://img.shields.io/nuget/v/SwiftMap?style=flat-square&color=004880&label=NuGet)](https://www.nuget.org/packages/SwiftMap)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](LICENSE)

_Mapeamento de propriedades por convenção, com expression trees compiladas — zero reflection em tempo de execução._
_O modo source generator atinge **paridade com código manual** em tempo de compilação (estilo Mapperly)._

</div>

---

## Por que SwiftMap?

- **Modo source generator** — a partial class `[Mapper]` emite os corpos de mapeamento em **tempo de compilação**; zero custo de inicialização, zero dispatch de delegates, zero reflection em chamada — idêntico a código escrito à mão
- **Modo runtime com expression trees** — os mapeamentos compilam uma vez para delegates nativos via [FastExpressionCompiler](https://github.com/dadhi/FastExpressionCompiler); chamadas subsequentes são livres de alocação
- **API fluente e descobrível** — `ForMember`, `Ignore`, `AfterMap`, `ReverseMap`, `NullSubstitute`, `Condition`, `Patch` e mais
- **Sem dependências pesadas** — pacote NuGet único; depende apenas de `Microsoft.Extensions.DependencyInjection.Abstractions`
- **Records e propriedades init-only** — suporte completo a records do C# 9+ via seleção do construtor primário
- **DI de primeira classe** — `services.AddSwiftMap(...)` com scan de perfis

---

## Instalação

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

## Início Rápido

### Modo Source Generator (recomendado — tempo de compilação, zero overhead)

```csharp
using SwiftMap;

[Mapper]
public partial class AppMapper
{
    public partial PersonDto   Map(Person source);
    public partial OrderDto    Map(Order source);
    public partial ProductDto  Map(Product source);
}

// Uso — sem DI, sem custo de inicialização, velocidade = código manual
var mapper = new AppMapper();
var dto = mapper.Map(person);
```

O atributo `[Mapper]` aciona o source generator Roslyn incluído no pacote. Ele analisa seus tipos em tempo de compilação e emite corpos eficientes com object initializers — suportando objetos planos, objetos aninhados (null-safe com is-pattern), coleções (for-loop, sem LINQ), records (construtor primário), enums e unwrapping de nullable.

### Modo Runtime com Expression Trees

```csharp
// 1. Cria o mapper — uma vez na inicialização ou via DI
var mapper = Mapper.Create(cfg =>
    cfg.CreateMap<PersonSource, PersonDest>());

// 2. Mapeia com inferência do tipo de origem
var dest = mapper.Map<PersonDest>(source);

// 3. Mapeia com parâmetros de tipo explícitos (ligeiramente mais rápido — sem chamada a GetType())
var dest = mapper.Map<PersonSource, PersonDest>(source);

// 4. Mapeia para uma instância existente
mapper.Map(source, existingDestination);
```

---

## Configuração

### Mapeamento por convenção — sem configuração necessária

Propriedades com o mesmo nome são conectadas automaticamente (case-insensitive):

```csharp
var mapper = Mapper.Create(_ => { });
var dto = mapper.Map<CustomerDto>(customer);
```

### API Fluente

```csharp
var mapper = Mapper.Create(cfg =>
    cfg.CreateMap<Customer, CustomerDto>(map =>
        map.ForMember(d => d.AddressCity,  opt => opt.MapFrom(s => s.Address!.City))
           .Ignore(d => d.InternalId)
           .ForMember(d => d.Name,         opt => opt.NullSubstitute("Desconhecido"))
           .ForMember(d => d.Score,        opt => opt.Condition(s => s.IsActive))
           .AfterMap((src, dest) => dest.Name = dest.Name.ToUpperInvariant())));
```

### Mapeamento reverso

```csharp
cfg.CreateMap<OrderDto, Order>()
   .ReverseMap(); // registra o mapeamento inverso automaticamente
```

### Perfis

Organize grandes conjuntos de mapeamentos em unidades coesas:

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

### Mapeamento por atributos

```csharp
[MapTo(typeof(ProductDto))]
public class Product { ... }

public class TargetDto
{
    [MapProperty("FullName")]   // mapeia de uma propriedade com nome diferente na origem
    public string Name { get; set; }

    [IgnoreMap]                 // ignorada durante o mapeamento
    public string Secret { get; set; }
}
```

### Injeção de Dependência

```csharp
// Configuração inline
services.AddSwiftMap(cfg => cfg.CreateMap<Order, OrderDto>());

// Scan de assemblies em busca de subclasses de MapProfile e atributos [MapTo]/[MapFrom]
services.AddSwiftMap(typeof(Program).Assembly);
```

### HTTP PATCH — Semântica de Patch

`Patch` aplica apenas os **campos não-nulos** da origem sobre uma instância de destino existente, preservando todo o resto. Projetado para endpoints HTTP PATCH onde o cliente envia apenas os campos que deseja alterar.

```csharp
// Apenas Name foi enviado — Age e Email são null (não fornecidos pelo cliente)
var updateDto = new UpdateUserDto { Name = "João", Age = null };
var user = await dbContext.Users.FindAsync(id); // { Name = "Maria", Age = 30, Email = "m@m.com" }

mapper.Patch(updateDto, user);
// Resultado: { Name = "João", Age = 30, Email = "m@m.com" }
// Age e Email foram preservados — estavam null no dto
```

Funciona automaticamente por convenção — sem necessidade de `CreateMap`. Para cenários avançados:

```csharp
// Ignora campos que correspondem ao valor default (0, false, Guid.Empty, etc.)
mapper.Patch(dto, entity, cfg => cfg.AsPatch(PatchBehavior.SkipDefaultFields));

// Registra o comportamento de patch em um perfil
public class AppProfile : MapProfile
{
    public AppProfile()
    {
        CreateMap<UpdateUserDto, User>().AsPatch();
    }
}
```

| Tipo do campo na origem | Comportamento |
|:------------------------|:--------------|
| `string` / tipo referência | Ignorado se `null` |
| `Nullable<T>` (`int?`, `bool?`, …) | Ignorado se `!HasValue` |
| Tipo valor não-nullable (`int`, `bool`, …) | Sempre aplicado (não pode ser null) |

---

## Benchmarks

Benchmarks comparados com **AutoMapper 13.0.1** e **Mapster 7.4.0** no .NET 9.

```
BenchmarkDotNet v0.15.8  ·  Windows 11  ·  AMD Ryzen 5 3600 3.60GHz (6C/12T)
.NET SDK 9.0.312  ·  .NET 9.0.14  ·  X64 RyuJIT x86-64-v3
ShortRun: 3 warmups + 7 iterações
```

### Objeto plano simples (7 propriedades)

| Método              |      Média |    Erro |   Desv.Pad | Razão         | Alocado |
|:--------------------|-----------:|--------:|-----------:|:--------------|--------:|
| Manual              |  15.19 ns | 4.863 ns | 2.159 ns | baseline      |      64 B |
| **SwiftGenerated**  |  15.27 ns | 5.110 ns | 2.269 ns | **1.03×**     |      64 B |
| Mapster             |  30.37 ns | 4.921 ns | 2.185 ns | 2.04× mais lento  |      64 B |
| SwiftMap (runtime)  |  39.75 ns | 8.234 ns | 3.656 ns | 2.67× mais lento  |      64 B |
| AutoMapper          |  89.95 ns |17.732 ns | 7.873 ns | 6.04× mais lento  |      64 B |

### Objeto aninhado (pai + filho)

| Método              |      Média |    Erro |   Desv.Pad | Razão         | Alocado |
|:--------------------|-----------:|--------:|-----------:|:--------------|--------:|
| Manual              |  22.18 ns | 6.680 ns | 2.966 ns | baseline      |     104 B |
| **SwiftGenerated**  |  22.97 ns | 6.421 ns | 2.851 ns | **1.05×**     |     104 B |
| Mapster             |  40.52 ns | 8.772 ns | 3.895 ns | 1.86× mais lento  |     104 B |
| SwiftMap (runtime)  |  48.76 ns |13.197 ns | 5.859 ns | 2.23× mais lento  |     104 B |
| AutoMapper          | 102.62 ns |18.835 ns | 8.363 ns | 4.70× mais lento  |     104 B |

### Mapeamento de coleções

| Método              | Qtd |      Média |     Erro |    Desv.Pad | Razão         | Alocado |
|:--------------------|----:|-----------:|---------:|------------:|:--------------|--------:|
| Manual              |   100 |  1.425 µs |  0.534 µs |  0.237 µs | baseline      |   7.05 KB |
| **SwiftGenerated**  |   100 |  1.519 µs |  0.573 µs |  0.254 µs | **1.09×**     |   7.05 KB |
| Mapster             |   100 |  2.637 µs |  0.542 µs |  0.241 µs | 1.90× mais lento  |   7.05 KB |
| SwiftMap (runtime)  |   100 |  4.372 µs |  0.968 µs |  0.430 µs | 3.14× mais lento  |   7.05 KB |
| AutoMapper          |   100 |  8.486 µs |  3.128 µs |  1.389 µs | 6.10× mais lento  |   7.05 KB |
| Manual              |  1000 | 14.202 µs |  5.038 µs |  2.237 µs | baseline      |  70.34 KB |
| **SwiftGenerated**  |  1000 | 15.540 µs |  4.086 µs |  1.814 µs | **1.12×**     |  70.34 KB |
| Mapster             |  1000 | 30.888 µs |  7.347 µs |  3.262 µs | 2.23× mais lento  |  70.34 KB |
| SwiftMap (runtime)  |  1000 | 43.908 µs | 13.642 µs |  6.057 µs | 3.17× mais lento  |  70.34 KB |
| AutoMapper          |  1000 | 91.405 µs | 14.038 µs |  6.233 µs | 6.59× mais lento  |  70.34 KB |

### Record (construtor primário)

| Método              |      Média |    Erro |   Desv.Pad | Razão         | Alocado |
|:--------------------|-----------:|--------:|-----------:|:--------------|--------:|
| Manual              |  11.27 ns | 4.766 ns | 2.116 ns | baseline      |      48 B |
| **SwiftGenerated**  |  12.89 ns | 4.260 ns | 1.891 ns | **1.18×**     |      48 B |
| Mapster             |  30.10 ns | 6.656 ns | 2.955 ns | 2.74× mais lento  |      48 B |
| SwiftMap (runtime)  |  37.35 ns | 7.100 ns | 3.153 ns | 3.40× mais lento  |      48 B |
| AutoMapper          |  86.29 ns |16.937 ns | 7.520 ns | 7.86× mais lento  |      48 B |

### Resumo

| Cenário           | SwiftGenerated vs Manual | SwiftGenerated vs Mapster | SwiftGenerated vs AutoMapper |
|:------------------|:------------------------:|:-------------------------:|:----------------------------:|
| Objeto simples    | **≈ paridade (1.03×)**   | **2.0× mais rápido**      | **5.9× mais rápido**         |
| Objeto aninhado   | **≈ paridade (1.05×)**   | **1.8× mais rápido**      | **4.5× mais rápido**         |
| Coleção ×1000     | **≈ paridade (1.12×)**   | **2.0× mais rápido**      | **5.9× mais rápido**         |
| Record            | **≈ paridade (1.18×)**   | **2.3× mais rápido**      | **6.7× mais rápido**         |

> Todas as medições: mesma memória alocada que o código manual (sem overhead).

---

## Estrutura do Projeto

```
src/
├── SwiftMap/                              # Mapeador runtime com expression trees
│   ├── Mapper.cs                          # Ponto de entrada — Mapper.Create(...)
│   ├── IMapper.cs                         # Interface pública
│   ├── MapperConfig.cs                    # Configuração + cache de delegates compilados
│   ├── TypeMapConfig.cs                   # API fluente: ForMember, Ignore, AfterMap...
│   ├── MapProfile.cs                      # Classe base para perfis
│   ├── Attributes/
│   │   └── MapToAttribute.cs              # [MapTo], [MapFrom], [IgnoreMap], [MapProperty], [Mapper]
│   ├── Extensions/
│   │   └── ServiceCollectionExtensions.cs # AddSwiftMap(...)
│   └── Internal/
│       ├── MappingCompiler.cs             # Compilador de expression trees (motor principal)
│       └── TypePair.cs                    # Chave (Origem, Destino) para dicionário
└── SwiftMap.SourceGenerator/              # Roslyn IIncrementalGenerator
    ├── MapperGenerator.cs                 # Ponto de entrada [Generator]
    ├── Models/                            # MapperClassModel, MappingMethodModel, MappedPropertyModel
    ├── Pipeline/
    │   └── MapperModelExtractor.cs        # Análise semântica
    └── Emit/
        ├── MappingBodyEmitter.cs          # Geração de código
        └── SourceWriter.cs                # StringBuilder com indentação
```

---

## Roadmap

- [x] **Integração com FastExpressionCompiler** — `CompileFast()` substitui `Expression.Compile()` para criação mais rápida de delegates
- [x] **Modo source generator** — partial class `[Mapper]` emite corpos de mapeamento em tempo de compilação; atinge paridade com mapeamento manual
- [x] **Semântica de Patch** — `mapper.Patch(dto, entity)` aplica apenas campos não-nulos; suporte nativo a endpoints HTTP PATCH
- [ ] **Mapeamento assíncrono** — `MapAsync<TDest>(source)` para pipelines que precisam de resolução assíncrona de valores
- [ ] **Projeção IQueryable** — `ProjectTo<TDest>()` para projeção em queries de ORM
- [ ] **Release no NuGet** — publicar `SwiftMap` no nuget.org

---

## Contribuindo

Contribuições são bem-vindas! Por favor:

1. Faça um fork e crie um branch de feature (`git checkout -b feature/minha-feature`)
2. Adicione ou atualize testes para cobrir sua mudança
3. Execute os benchmarks para verificar que não houve regressão de performance:
   ```bash
   dotnet run -c Release --project benchmarks/SwiftMap.Benchmarks
   ```
4. Abra um pull request com uma descrição clara do que foi alterado e por quê

Para mudanças significativas, abra uma issue primeiro para discutir a abordagem.

---

## Licença

SwiftMap é distribuído sob a [Licença MIT](LICENSE).
