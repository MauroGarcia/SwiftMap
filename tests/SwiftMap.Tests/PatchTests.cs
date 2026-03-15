namespace SwiftMap.Tests;

#region Patch Test Models

public class UserEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
}

public class UpdateUserDto
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public int? Age { get; set; }
}

public class PartialUserDto
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public int? Age { get; set; }
    public string? Extra { get; set; }
}

public class ValueTypeEntity
{
    public int Count { get; set; }
    public bool Active { get; set; }
    public double Score { get; set; }
}

public class ValueTypeDto
{
    public int Count { get; set; }
    public bool Active { get; set; }
    public double Score { get; set; }
}

public class MixedEntity
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public string? Tag { get; set; }
}

public class MixedDto
{
    public string? Name { get; set; }
    public int Count { get; set; }
    public string? Tag { get; set; }
}

public class SkipDefaultEntity
{
    public int Value { get; set; }
    public bool Flag { get; set; }
    public string Name { get; set; } = "";
}

public class SkipDefaultDto
{
    public int Value { get; set; }
    public bool Flag { get; set; }
    public string? Name { get; set; }
}

#endregion

/// <summary>
/// Tests for Patch semantics — only non-null source fields overwrite the destination.
/// </summary>
public class PatchTests
{
    /// <summary>
    /// Null string field in source must NOT overwrite the existing destination value.
    /// </summary>
    [Fact]
    public void Patch_NullSourceField_PreservesDestinationValue()
    {
        var mapper = Mapper.Create(_ => { });

        var dto = new UpdateUserDto { Name = null, Email = null, Age = null };
        var entity = new UserEntity { Id = 1, Name = "Maria", Email = "m@m.com", Age = 30 };

        mapper.Patch(dto, entity);

        Assert.Equal("Maria", entity.Name);
        Assert.Equal("m@m.com", entity.Email);
        Assert.Equal(30, entity.Age);
    }

    /// <summary>
    /// Non-null fields in source MUST overwrite the destination.
    /// </summary>
    [Fact]
    public void Patch_NonNullSourceField_OverwritesDestinationValue()
    {
        var mapper = Mapper.Create(_ => { });

        var dto = new UpdateUserDto { Name = "João", Email = "j@j.com", Age = 25 };
        var entity = new UserEntity { Id = 1, Name = "Maria", Email = "m@m.com", Age = 30 };

        mapper.Patch(dto, entity);

        Assert.Equal("João", entity.Name);
        Assert.Equal("j@j.com", entity.Email);
        Assert.Equal(25, entity.Age);
    }

    /// <summary>
    /// Nullable&lt;T&gt; with null value must NOT overwrite destination.
    /// </summary>
    [Fact]
    public void Patch_NullableValueType_NullPreservesDestination()
    {
        var mapper = Mapper.Create(_ => { });

        var dto = new UpdateUserDto { Name = "João", Age = null };
        var entity = new UserEntity { Id = 1, Name = "Maria", Age = 30 };

        mapper.Patch(dto, entity);

        // Age was null in dto → destination Age must be preserved
        Assert.Equal(30, entity.Age);
        // Name was set → must overwrite
        Assert.Equal("João", entity.Name);
    }

    /// <summary>
    /// Nullable&lt;T&gt; with a value (HasValue=true) MUST overwrite destination.
    /// </summary>
    [Fact]
    public void Patch_NullableValueType_HasValueOverwritesDestination()
    {
        var mapper = Mapper.Create(_ => { });

        var dto = new UpdateUserDto { Age = 42 };
        var entity = new UserEntity { Id = 1, Name = "Maria", Age = 10 };

        mapper.Patch(dto, entity);

        Assert.Equal(42, entity.Age);
    }

    /// <summary>
    /// Non-nullable value types (int, bool, double, etc.) are always assigned,
    /// because they cannot be null.
    /// </summary>
    [Fact]
    public void Patch_NonNullableValueType_AlwaysOverwrites()
    {
        var mapper = Mapper.Create(_ => { });

        var dto = new ValueTypeDto { Count = 0, Active = false, Score = 0.0 };
        var entity = new ValueTypeEntity { Count = 5, Active = true, Score = 9.9 };

        mapper.Patch(dto, entity);

        // Non-nullable value types — even zero/false are written
        Assert.Equal(0, entity.Count);
        Assert.False(entity.Active);
        Assert.Equal(0.0, entity.Score);
    }

    /// <summary>
    /// Partial update: only the fields that are non-null in source should be applied.
    /// </summary>
    [Fact]
    public void Patch_PartialUpdate_OnlyProvidedFieldsUpdated()
    {
        var mapper = Mapper.Create(_ => { });

        // HTTP PATCH scenario: only Name was sent, Age and Email are null (not sent)
        var dto = new UpdateUserDto { Name = "Updated Name", Email = null, Age = null };
        var entity = new UserEntity { Id = 99, Name = "Original", Email = "orig@test.com", Age = 25 };

        mapper.Patch(dto, entity);

        Assert.Equal("Updated Name", entity.Name);    // updated
        Assert.Equal("orig@test.com", entity.Email);  // preserved
        Assert.Equal(25, entity.Age);                 // preserved
        Assert.Equal(99, entity.Id);                  // untouched (not in dto)
    }

    /// <summary>
    /// Patch must work without any prior CreateMap registration.
    /// </summary>
    [Fact]
    public void Patch_WithoutPriorCreateMap_WorksAutomatically()
    {
        // No CreateMap — Patch should still compile and apply correctly
        var mapper = Mapper.Create(_ => { });

        var dto = new MixedDto { Name = "Auto", Count = 7, Tag = null };
        var entity = new MixedEntity { Name = "Old", Count = 0, Tag = "keep-me" };

        mapper.Patch(dto, entity);

        Assert.Equal("Auto", entity.Name);
        Assert.Equal(7, entity.Count);
        Assert.Equal("keep-me", entity.Tag); // null in dto — preserved
    }

    /// <summary>
    /// With PatchBehavior.SkipDefaultFields, even non-nullable value types that hold default(T)
    /// (0, false, Guid.Empty) should not overwrite the destination.
    /// </summary>
    [Fact]
    public void Patch_SkipDefaultBehavior_IgnoresDefaultValueTypes()
    {
        var mapper = Mapper.Create(cfg =>
            cfg.CreateMap<SkipDefaultDto, SkipDefaultEntity>(map =>
                map.AsPatch(PatchBehavior.SkipDefaultFields)));

        var dto = new SkipDefaultDto { Value = 0, Flag = false, Name = "provided" };
        var entity = new SkipDefaultEntity { Value = 42, Flag = true, Name = "original" };

        mapper.Patch(dto, entity);

        // Value=0 and Flag=false are defaults — must be preserved
        Assert.Equal(42, entity.Value);
        Assert.True(entity.Flag);
        // Name="provided" is non-default — must be written
        Assert.Equal("provided", entity.Name);
    }
}
