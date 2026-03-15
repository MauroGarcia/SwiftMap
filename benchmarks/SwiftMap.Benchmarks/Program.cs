using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Order;
using Mapster;

// Alias to avoid namespace conflict with AutoMapper.IMapper
using SwiftIMapper = SwiftMap.IMapper;
using AM = AutoMapper;

// ShortRun: 3 warmups + 5 iterations — finishes in ~3 min total, still statistically sound
var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.ShortRun.WithWarmupCount(3).WithIterationCount(7))
    .WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend))
    .AddDiagnoser(MemoryDiagnoser.Default)
    .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));

BenchmarkRunner.Run(
[
    typeof(SimpleObjectBenchmark),
    typeof(NestedObjectBenchmark),
    typeof(CollectionBenchmark),
    typeof(RecordBenchmark),
], config);

// ─────────────────────────────────────────────
// SOURCE-GENERATED MAPPER
// ─────────────────────────────────────────────

[SwiftMap.Mapper]
public partial class BenchMapper
{
    public partial PersonDest   MapPerson(PersonSource source);
    public partial OrderDest    MapOrder(OrderSource source);
    public partial PersonRecordDest MapRecord(PersonRecord source);
}

// ─────────────────────────────────────────────
// MODELS
// ─────────────────────────────────────────────

public class PersonSource
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public double Score { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PersonDest
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public double Score { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AddressSource
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
    public string PostalCode { get; set; } = "";
}

public class AddressDest
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
    public string PostalCode { get; set; } = "";
}

public class OrderSource
{
    public int Id { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public AddressSource? ShippingAddress { get; set; }
}

public class OrderDest
{
    public int Id { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public AddressDest? ShippingAddress { get; set; }
}

public record PersonRecord(int Id, string FirstName, string LastName, string Email, int Age);
public record PersonRecordDest(int Id, string FirstName, string LastName, string Email, int Age);

// ─────────────────────────────────────────────
// BENCHMARK 1: Simple flat object (7 props)
// ─────────────────────────────────────────────

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[HideColumns(Column.StdErr, Column.Min, Column.Max, Column.Median)]
public class SimpleObjectBenchmark
{
    private PersonSource _src = null!;
    private SwiftIMapper _swift = null!;
    private AM.IMapper _auto = null!;
    private static readonly BenchMapper _gen = new();

    [GlobalSetup]
    public void Setup()
    {
        _src = new PersonSource
        {
            Id = 1, FirstName = "John", LastName = "Doe",
            Email = "john@example.com", Age = 30, Score = 95.5,
            CreatedAt = DateTime.UtcNow
        };

        _swift = SwiftMap.Mapper.Create(cfg =>
            cfg.CreateMap<PersonSource, PersonDest>());

        var amConfig = new AM.MapperConfiguration(cfg =>
            cfg.CreateMap<PersonSource, PersonDest>());
        amConfig.CompileMappings();
        _auto = amConfig.CreateMapper();

        // Warm up
        _src.Adapt<PersonDest>();
        _swift.Map<PersonDest>(_src);
        _auto.Map<PersonDest>(_src);
        _gen.MapPerson(_src);
    }

    [Benchmark(Baseline = true)]
    public PersonDest Manual()
    {
        var s = _src;
        return new PersonDest
        {
            Id = s.Id, FirstName = s.FirstName, LastName = s.LastName,
            Email = s.Email, Age = s.Age, Score = s.Score, CreatedAt = s.CreatedAt
        };
    }

    [Benchmark]
    public PersonDest SwiftGenerated() => _gen.MapPerson(_src);

    [Benchmark]
    public PersonDest Swift() => _swift.Map<PersonDest>(_src);

    [Benchmark]
    public PersonDest Mapster_Adapt() => _src.Adapt<PersonDest>();

    [Benchmark]
    public PersonDest AutoMapper_Map() => _auto.Map<PersonDest>(_src);
}

// ─────────────────────────────────────────────
// BENCHMARK 2: Nested object (parent + child)
// ─────────────────────────────────────────────

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[HideColumns(Column.StdErr, Column.Min, Column.Max, Column.Median)]
public class NestedObjectBenchmark
{
    private OrderSource _src = null!;
    private SwiftIMapper _swift = null!;
    private AM.IMapper _auto = null!;
    private static readonly BenchMapper _gen = new();

    [GlobalSetup]
    public void Setup()
    {
        _src = new OrderSource
        {
            Id = 42, ProductName = "Laptop", Price = 999.99m, Quantity = 2,
            ShippingAddress = new AddressSource
            {
                Street = "123 Main St", City = "New York",
                Country = "US", PostalCode = "10001"
            }
        };

        _swift = SwiftMap.Mapper.Create(cfg =>
        {
            cfg.CreateMap<AddressSource, AddressDest>();
            cfg.CreateMap<OrderSource, OrderDest>();
        });

        var amConfig = new AM.MapperConfiguration(cfg =>
        {
            cfg.CreateMap<AddressSource, AddressDest>();
            cfg.CreateMap<OrderSource, OrderDest>();
        });
        amConfig.CompileMappings();
        _auto = amConfig.CreateMapper();

        // Warm up
        _src.Adapt<OrderDest>();
        _swift.Map<OrderDest>(_src);
        _auto.Map<OrderDest>(_src);
        _gen.MapOrder(_src);
    }

    [Benchmark(Baseline = true)]
    public OrderDest Manual()
    {
        var s = _src;
        return new OrderDest
        {
            Id = s.Id, ProductName = s.ProductName, Price = s.Price, Quantity = s.Quantity,
            ShippingAddress = s.ShippingAddress is { } a ? new AddressDest
            {
                Street = a.Street, City = a.City, Country = a.Country, PostalCode = a.PostalCode
            } : null
        };
    }

    [Benchmark]
    public OrderDest SwiftGenerated() => _gen.MapOrder(_src);

    [Benchmark]
    public OrderDest Swift() => _swift.Map<OrderDest>(_src);

    [Benchmark]
    public OrderDest Mapster_Adapt() => _src.Adapt<OrderDest>();

    [Benchmark]
    public OrderDest AutoMapper_Map() => _auto.Map<OrderDest>(_src);
}

// ─────────────────────────────────────────────
// BENCHMARK 3: Collection (N items)
// ─────────────────────────────────────────────

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[HideColumns(Column.StdErr, Column.Min, Column.Max, Column.Median)]
public class CollectionBenchmark
{
    private PersonSource[] _srcs = null!;
    private SwiftIMapper _swift = null!;
    private AM.IMapper _auto = null!;
    private static readonly BenchMapper _gen = new();

    [Params(100, 1000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _srcs = Enumerable.Range(1, 1000).Select(i => new PersonSource
        {
            Id = i, FirstName = $"First{i}", LastName = $"Last{i}",
            Email = $"user{i}@test.com", Age = 20 + i % 50,
            Score = i * 0.1, CreatedAt = DateTime.UtcNow
        }).ToArray();

        _swift = SwiftMap.Mapper.Create(cfg =>
            cfg.CreateMap<PersonSource, PersonDest>());

        var amConfig = new AM.MapperConfiguration(cfg =>
            cfg.CreateMap<PersonSource, PersonDest>());
        amConfig.CompileMappings();
        _auto = amConfig.CreateMapper();

        // Warm up (single item)
        _srcs[0].Adapt<PersonDest>();
        _swift.Map<PersonDest>(_srcs[0]);
        _auto.Map<PersonDest>(_srcs[0]);
        _gen.MapPerson(_srcs[0]);
    }

    [Benchmark(Baseline = true)]
    public PersonDest[] Manual()
    {
        var items = _srcs.AsSpan(0, Count);
        var result = new PersonDest[Count];
        for (int i = 0; i < Count; i++)
        {
            var s = items[i];
            result[i] = new PersonDest
            {
                Id = s.Id, FirstName = s.FirstName, LastName = s.LastName,
                Email = s.Email, Age = s.Age, Score = s.Score, CreatedAt = s.CreatedAt
            };
        }
        return result;
    }

    [Benchmark]
    public PersonDest[] SwiftGenerated()
    {
        var items = _srcs.AsSpan(0, Count);
        var result = new PersonDest[Count];
        for (int i = 0; i < Count; i++)
            result[i] = _gen.MapPerson(items[i]);
        return result;
    }

    [Benchmark]
    public PersonDest[] Swift()
    {
        var items = _srcs.AsSpan(0, Count);
        var result = new PersonDest[Count];
        for (int i = 0; i < Count; i++)
            result[i] = _swift.Map<PersonDest>(items[i]);
        return result;
    }

    [Benchmark]
    public PersonDest[] Mapster_Adapt()
    {
        var items = _srcs.AsSpan(0, Count);
        var result = new PersonDest[Count];
        for (int i = 0; i < Count; i++)
            result[i] = items[i].Adapt<PersonDest>();
        return result;
    }

    [Benchmark]
    public PersonDest[] AutoMapper_Map()
    {
        var items = _srcs.AsSpan(0, Count);
        var result = new PersonDest[Count];
        for (int i = 0; i < items.Length; i++)
            result[i] = _auto.Map<PersonDest>(items[i]);
        return result;
    }
}

// ─────────────────────────────────────────────
// BENCHMARK 4: Record (primary constructor)
// ─────────────────────────────────────────────

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[HideColumns(Column.StdErr, Column.Min, Column.Max, Column.Median)]
public class RecordBenchmark
{
    private PersonRecord _src = null!;
    private SwiftIMapper _swift = null!;
    private AM.IMapper _auto = null!;
    private static readonly BenchMapper _gen = new();

    [GlobalSetup]
    public void Setup()
    {
        _src = new PersonRecord(1, "Jane", "Doe", "jane@example.com", 28);

        _swift = SwiftMap.Mapper.Create(cfg =>
            cfg.CreateMap<PersonRecord, PersonRecordDest>());

        var amConfig = new AM.MapperConfiguration(cfg =>
            cfg.CreateMap<PersonRecord, PersonRecordDest>());
        amConfig.CompileMappings();
        _auto = amConfig.CreateMapper();

        // Warm up
        _src.Adapt<PersonRecordDest>();
        _swift.Map<PersonRecordDest>(_src);
        _auto.Map<PersonRecordDest>(_src);
        _gen.MapRecord(_src);
    }

    [Benchmark(Baseline = true)]
    public PersonRecordDest Manual() =>
        new(_src.Id, _src.FirstName, _src.LastName, _src.Email, _src.Age);

    [Benchmark]
    public PersonRecordDest SwiftGenerated() => _gen.MapRecord(_src);

    [Benchmark]
    public PersonRecordDest Swift() => _swift.Map<PersonRecordDest>(_src);

    [Benchmark]
    public PersonRecordDest Mapster_Adapt() => _src.Adapt<PersonRecordDest>();

    [Benchmark]
    public PersonRecordDest AutoMapper_Map() => _auto.Map<PersonRecordDest>(_src);
}
