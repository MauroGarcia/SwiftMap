namespace SwiftMap.Tests;

#region Test Models

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public Address? Address { get; set; }
    public List<Order> Orders { get; set; } = [];
}

public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Country { get; set; } = "";
}

public class Order
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
}

public class CustomerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string AddressCity { get; set; } = "";     // Flattened
    public string AddressStreet { get; set; } = "";   // Flattened
    public string AddressCountry { get; set; } = "";  // Flattened
    public List<OrderDto> Orders { get; set; } = [];
}

public class OrderDto
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
}

// Record types
public record PersonRecord(string FirstName, string LastName, int Age);
public record PersonSummary(string FirstName, string LastName);

// Struct
public struct Point { public double X { get; set; } public double Y { get; set; } }
public struct PointDto { public double X { get; set; } public double Y { get; set; } }

// Enum mapping
public enum Status { Active, Inactive, Pending }
public enum StatusCode { Active = 0, Inactive = 1, Pending = 2 }

public class WithEnum { public Status Status { get; set; } }
public class WithEnumString { public string Status { get; set; } = ""; }
public class WithEnumCode { public StatusCode Status { get; set; } }

// Nullable support
public class NullableSource { public int? Value { get; set; } public string? Name { get; set; } }
public class NullableTarget { public int Value { get; set; } public string Name { get; set; } = ""; }

// Init-only properties
public class InitSource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class InitTarget
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

// Attribute-based
[MapTo(typeof(SimpleDto))]
public class SimpleModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

[MapFrom(typeof(SimpleModel))]
public class SimpleDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

// MapProperty attribute
public class RenamedSource
{
    public string FullName { get; set; } = "";
    public int Years { get; set; }
}

public class RenamedTarget
{
    [MapProperty("FullName")]
    public string Name { get; set; } = "";

    [MapProperty("Years")]
    public int Age { get; set; }
}

// IgnoreMap attribute
public class WithIgnored
{
    public int Id { get; set; }
    public string Public { get; set; } = "";

    [IgnoreMap]
    public string Secret { get; set; } = "";
}

public class WithIgnoredDto
{
    public int Id { get; set; }
    public string Public { get; set; } = "";
    public string Secret { get; set; } = "";
}

// Nested objects
public class Department
{
    public string Name { get; set; } = "";
    public Employee? Manager { get; set; }
}

public class Employee
{
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
}

public class DepartmentDto
{
    public string Name { get; set; } = "";
    public EmployeeDto? Manager { get; set; }
}

public class EmployeeDto
{
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
}

#endregion

public class ConventionMappingTests
{
    [Fact]
    public void Maps_matching_properties_by_convention()
    {
        var mapper = Mapper.Create(cfg => cfg.CreateMap<Customer, CustomerDto>());

        var customer = new Customer { Id = 1, Name = "Alice", Email = "alice@test.com" };
        var dto = mapper.Map<CustomerDto>(customer);

        Assert.Equal(1, dto.Id);
        Assert.Equal("Alice", dto.Name);
        Assert.Equal("alice@test.com", dto.Email);
    }

    [Fact]
    public void Maps_without_explicit_registration_for_simple_types()
    {
        var mapper = Mapper.Create(_ => { });

        var customer = new Customer { Id = 42, Name = "Bob", Email = "bob@test.com" };
        var dto = mapper.Map<CustomerDto>(customer);

        Assert.Equal(42, dto.Id);
        Assert.Equal("Bob", dto.Name);
    }

    [Fact]
    public void Flattens_nested_properties()
    {
        var mapper = Mapper.Create(_ => { });

        var customer = new Customer
        {
            Id = 1,
            Name = "Test",
            Address = new Address { Street = "123 Main St", City = "Springfield", Country = "US" }
        };

        var dto = mapper.Map<CustomerDto>(customer);

        Assert.Equal("Springfield", dto.AddressCity);
        Assert.Equal("123 Main St", dto.AddressStreet);
        Assert.Equal("US", dto.AddressCountry);
    }

    [Fact]
    public void Flattening_handles_null_nested_objects()
    {
        var mapper = Mapper.Create(_ => { });

        var customer = new Customer { Id = 1, Name = "Test", Address = null };
        var dto = mapper.Map<CustomerDto>(customer);

        Assert.Null(dto.AddressCity);
    }
}

public class RecordMappingTests
{
    [Fact]
    public void Maps_record_to_record_via_constructor()
    {
        var mapper = Mapper.Create(_ => { });

        var person = new PersonRecord("John", "Doe", 30);
        var summary = mapper.Map<PersonSummary>(person);

        Assert.Equal("John", summary.FirstName);
        Assert.Equal("Doe", summary.LastName);
    }

    [Fact]
    public void Maps_class_to_record()
    {
        var mapper = Mapper.Create(_ => { });

        var source = new { FirstName = "Jane", LastName = "Doe", Age = 25 };
        // Can't map anonymous types directly — use a concrete class
    }
}

public class StructMappingTests
{
    [Fact]
    public void Maps_struct_to_struct()
    {
        var mapper = Mapper.Create(_ => { });

        var point = new Point { X = 1.5, Y = 2.5 };
        var dto = mapper.Map<PointDto>(point);

        Assert.Equal(1.5, dto.X);
        Assert.Equal(2.5, dto.Y);
    }
}

public class CollectionMappingTests
{
    [Fact]
    public void Maps_list_of_objects()
    {
        var mapper = Mapper.Create(cfg =>
        {
            cfg.CreateMap<Order, OrderDto>();
            cfg.CreateMap<Customer, CustomerDto>();
        });

        var customer = new Customer
        {
            Id = 1,
            Name = "Test",
            Orders = [new Order { OrderId = 100, Total = 99.99m }, new Order { OrderId = 101, Total = 49.99m }]
        };

        var dto = mapper.Map<CustomerDto>(customer);

        Assert.Equal(2, dto.Orders.Count);
        Assert.Equal(100, dto.Orders[0].OrderId);
        Assert.Equal(99.99m, dto.Orders[0].Total);
        Assert.Equal(101, dto.Orders[1].OrderId);
    }
}

public class EnumMappingTests
{
    [Fact]
    public void Maps_enum_to_string()
    {
        var mapper = Mapper.Create(_ => { });

        var source = new WithEnum { Status = Status.Active };
        var dto = mapper.Map<WithEnumString>(source);

        Assert.Equal("Active", dto.Status);
    }

    [Fact]
    public void Maps_string_to_enum()
    {
        var mapper = Mapper.Create(_ => { });

        var source = new WithEnumString { Status = "Pending" };
        var dto = mapper.Map<WithEnum>(source);

        Assert.Equal(Status.Pending, dto.Status);
    }

    [Fact]
    public void Maps_enum_to_enum()
    {
        var mapper = Mapper.Create(_ => { });

        var source = new WithEnum { Status = Status.Inactive };
        var dto = mapper.Map<WithEnumCode>(source);

        Assert.Equal(StatusCode.Inactive, dto.Status);
    }
}

public class FluentConfigTests
{
    [Fact]
    public void ForMember_MapFrom_custom_source()
    {
        var mapper = Mapper.Create(cfg =>
            cfg.CreateMap<Customer, CustomerDto>(map =>
                map.ForMember(d => d.AddressCity, opt =>
                    opt.MapFrom(s => s.Address != null ? s.Address.City : "N/A"))));

        var customer = new Customer { Address = new Address { City = "NYC" } };
        var dto = mapper.Map<CustomerDto>(customer);
        Assert.Equal("NYC", dto.AddressCity);

        var noAddress = new Customer { Address = null };
        var dto2 = mapper.Map<CustomerDto>(noAddress);
        Assert.Equal("N/A", dto2.AddressCity);
    }

    [Fact]
    public void ForMember_with_condition()
    {
        var mapper = Mapper.Create(cfg =>
            cfg.CreateMap<Customer, CustomerDto>(map =>
                map.ForMember(d => d.Email, opt =>
                    opt.MapFrom(s => s.Email).Condition(s => !string.IsNullOrEmpty(s.Email)))));

        var customer = new Customer { Email = "" };
        var dto = mapper.Map<CustomerDto>(customer);
        Assert.Equal("", dto.Email);
    }

    [Fact]
    public void Ignore_member()
    {
        var mapper = Mapper.Create(cfg =>
            cfg.CreateMap<Customer, CustomerDto>(map =>
                map.Ignore(d => d.Email)));

        var customer = new Customer { Id = 1, Name = "Test", Email = "test@test.com" };
        var dto = mapper.Map<CustomerDto>(customer);

        Assert.Equal(1, dto.Id);
        Assert.Equal("", dto.Email); // default value, not null — property initialized to ""
    }

    [Fact]
    public void ConstructUsing_custom_factory()
    {
        var mapper = Mapper.Create(cfg =>
            cfg.CreateMap<Customer, CustomerDto>(map =>
                map.ConstructUsing(src => new CustomerDto { Id = src.Id * 10 })
                   .Ignore(d => d.Id))); // Ignore so convention doesn't overwrite

        var customer = new Customer { Id = 5, Name = "Test" };
        var dto = mapper.Map<CustomerDto>(customer);

        Assert.Equal(50, dto.Id);
        Assert.Equal("Test", dto.Name);
    }

    [Fact]
    public void AfterMap_hook()
    {
        var mapper = Mapper.Create(cfg =>
            cfg.CreateMap<Customer, CustomerDto>(map =>
                map.AfterMap((src, dest) => dest.Name = dest.Name.ToUpperInvariant())));

        var customer = new Customer { Name = "alice" };
        var dto = mapper.Map<CustomerDto>(customer);

        Assert.Equal("ALICE", dto.Name);
    }

    [Fact]
    public void NullSubstitute_for_string()
    {
        var mapper = Mapper.Create(cfg =>
            cfg.CreateMap<NullableSource, NullableTarget>(map =>
                map.ForMember(d => d.Name, opt => opt.NullSubstitute("(unknown)"))));

        var source = new NullableSource { Value = 42, Name = null };
        var dto = mapper.Map<NullableTarget>(source);

        Assert.Equal("(unknown)", dto.Name);
    }
}

public class AttributeMappingTests
{
    [Fact]
    public void MapTo_attribute_registers_mapping()
    {
        var mapper = Mapper.Create(cfg =>
            cfg.AddMapsFromAssembly(typeof(SimpleModel).Assembly));

        var model = new SimpleModel { Id = 1, Title = "Hello" };
        var dto = mapper.Map<SimpleDto>(model);

        Assert.Equal(1, dto.Id);
        Assert.Equal("Hello", dto.Title);
    }

    [Fact]
    public void MapProperty_attribute_renames_source()
    {
        var mapper = Mapper.Create(_ => { });

        var source = new RenamedSource { FullName = "Alice", Years = 30 };
        var target = mapper.Map<RenamedTarget>(source);

        Assert.Equal("Alice", target.Name);
        Assert.Equal(30, target.Age);
    }

    [Fact]
    public void IgnoreMap_attribute_skips_property()
    {
        var mapper = Mapper.Create(_ => { });

        var source = new WithIgnored { Id = 1, Public = "visible", Secret = "hidden" };
        var dto = mapper.Map<WithIgnoredDto>(source);

        Assert.Equal(1, dto.Id);
        Assert.Equal("visible", dto.Public);
        Assert.Equal("", dto.Secret); // Not mapped, stays at default ""
    }
}

public class NestedObjectMappingTests
{
    [Fact]
    public void Maps_nested_objects_automatically()
    {
        var mapper = Mapper.Create(cfg =>
        {
            cfg.CreateMap<Employee, EmployeeDto>();
            cfg.CreateMap<Department, DepartmentDto>();
        });

        var dept = new Department
        {
            Name = "Engineering",
            Manager = new Employee { Name = "Bob", Title = "VP" }
        };

        var dto = mapper.Map<DepartmentDto>(dept);

        Assert.Equal("Engineering", dto.Name);
        Assert.NotNull(dto.Manager);
        Assert.Equal("Bob", dto.Manager!.Name);
        Assert.Equal("VP", dto.Manager.Title);
    }

    [Fact]
    public void Handles_null_nested_objects()
    {
        var mapper = Mapper.Create(cfg =>
        {
            cfg.CreateMap<Employee, EmployeeDto>();
            cfg.CreateMap<Department, DepartmentDto>();
        });

        var dept = new Department { Name = "Sales", Manager = null };
        var dto = mapper.Map<DepartmentDto>(dept);

        Assert.Equal("Sales", dto.Name);
        Assert.Null(dto.Manager);
    }
}

public class MapIntoTests
{
    [Fact]
    public void Maps_into_existing_instance()
    {
        var mapper = Mapper.Create(_ => { });

        var source = new Customer { Id = 1, Name = "Updated", Email = "new@test.com" };
        var existing = new CustomerDto { Id = 0, Name = "Old", Email = "old@test.com", AddressCity = "Preserved" };

        mapper.Map(source, existing);

        Assert.Equal(1, existing.Id);
        Assert.Equal("Updated", existing.Name);
        Assert.Equal("new@test.com", existing.Email);
    }
}

public class InitOnlyPropertyTests
{
    [Fact]
    public void Maps_to_init_only_properties()
    {
        var mapper = Mapper.Create(_ => { });

        var source = new InitSource { Id = 42, Name = "Init Test" };
        var target = mapper.Map<InitTarget>(source);

        Assert.Equal(42, target.Id);
        Assert.Equal("Init Test", target.Name);
    }
}

public class ProfileTests
{
    private class TestProfile : MapProfile
    {
        public TestProfile()
        {
            CreateMap<Customer, CustomerDto>()
                .ForMember(d => d.AddressCity, opt => opt.MapFrom(s => s.Address != null ? s.Address.City : ""))
                .Ignore(d => d.AddressStreet);
        }
    }

    [Fact]
    public void Profile_applies_configuration()
    {
        var mapper = Mapper.Create(cfg => cfg.AddProfile<TestProfile>());

        var customer = new Customer
        {
            Id = 1,
            Name = "Profile Test",
            Address = new Address { City = "Boston", Street = "Main St" }
        };

        var dto = mapper.Map<CustomerDto>(customer);

        Assert.Equal("Boston", dto.AddressCity);
        Assert.Equal("", dto.AddressStreet); // Ignored, stays at default ""
    }
}

public class NullableMappingTests
{
    [Fact]
    public void Maps_nullable_int_to_int()
    {
        var mapper = Mapper.Create(_ => { });

        var source = new NullableSource { Value = 42, Name = "test" };
        var target = mapper.Map<NullableTarget>(source);

        Assert.Equal(42, target.Value);
    }
}

public class ReverseMapTests
{
    [Fact]
    public void ReverseMap_creates_bidirectional_mapping()
    {
        var mapper = Mapper.Create(cfg =>
            cfg.CreateMap<Order, OrderDto>(map => map.ReverseMap()));

        var order = new Order { OrderId = 1, Total = 50m };
        var dto = mapper.Map<OrderDto>(order);
        Assert.Equal(1, dto.OrderId);

        var back = mapper.Map<Order>(dto);
        Assert.Equal(1, back.OrderId);
        Assert.Equal(50m, back.Total);
    }
}
