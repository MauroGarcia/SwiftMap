using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SwiftMap.SourceGenerator;

namespace SwiftMap.SourceGenerator.Tests;

public class GeneratorTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new MapperGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);

        var result = driver.GetRunResult();
        var generated = result.GeneratedTrees.FirstOrDefault()?.GetText().ToString() ?? "";
        var diags = result.Diagnostics;
        return (diags, generated);
    }

    private static MetadataReference[] GetReferences()
    {
        var refs = new System.Collections.Generic.List<MetadataReference>();

        // Core runtime assemblies
        refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(System.Enum).Assembly.Location));

        // System.Runtime
        var sysRuntime = Assembly.Load("System.Runtime");
        refs.Add(MetadataReference.CreateFromFile(sysRuntime.Location));

        // SwiftMap (provides [Mapper], [IgnoreMap], etc.)
        refs.Add(MetadataReference.CreateFromFile(typeof(SwiftMap.MapperAttribute).Assembly.Location));

        return refs.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1: flat object → object initializer
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FlatObject_EmitsObjectInitializer()
    {
        const string source = """
            using SwiftMap;

            public class PersonSrc  { public int Id { get; set; } public string Name { get; set; } = ""; }
            public class PersonDest { public int Id { get; set; } public string Name { get; set; } = ""; }

            [Mapper]
            public partial class AppMapper
            {
                public partial PersonDest Map(PersonSrc source);
            }
            """;

        var (diags, gen) = RunGenerator(source);

        Assert.Empty(diags.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("return new global::PersonDest", gen);
        Assert.Contains("Id = source.Id", gen);
        Assert.Contains("Name = source.Name", gen);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2: record → primary constructor
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Record_EmitsPrimaryConstructorCall()
    {
        const string source = """
            using SwiftMap;

            public class PersonSrc { public int Id { get; set; } public string Name { get; set; } = ""; }
            public record PersonRecord(int Id, string Name);

            [Mapper]
            public partial class AppMapper
            {
                public partial PersonRecord Map(PersonSrc source);
            }
            """;

        var (diags, gen) = RunGenerator(source);

        Assert.Empty(diags.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("return new global::PersonRecord(", gen);
        // Constructor params use the actual names from the record declaration (PascalCase here)
        Assert.Contains("Id: source.Id", gen);
        Assert.Contains("Name: source.Name", gen);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3: nested object → is-pattern null check
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NestedObject_EmitsIsPatternNullCheck()
    {
        const string source = """
            using SwiftMap;

            public class AddressSrc  { public string City { get; set; } = ""; }
            public class AddressDest { public string City { get; set; } = ""; }

            public class OrderSrc  { public int Id { get; set; } public AddressSrc? Addr { get; set; } }
            public class OrderDest { public int Id { get; set; } public AddressDest? Addr { get; set; } }

            [Mapper]
            public partial class AppMapper
            {
                public partial OrderDest Map(OrderSrc source);
            }
            """;

        var (diags, gen) = RunGenerator(source);

        Assert.Empty(diags.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("is { }", gen);
        Assert.Contains(": null", gen);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 4: [IgnoreMap] skips the property
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IgnoreMap_SkipsProperty()
    {
        const string source = """
            using SwiftMap;

            public class Src  { public int Id { get; set; } public string Secret { get; set; } = ""; }
            public class Dst  { public int Id { get; set; } [IgnoreMap] public string Secret { get; set; } = ""; }

            [Mapper]
            public partial class AppMapper
            {
                public partial Dst Map(Src source);
            }
            """;

        var (diags, gen) = RunGenerator(source);

        Assert.Empty(diags.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Id = source.Id", gen);
        Assert.DoesNotContain("Secret", gen);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 5: enum → enum same type (direct)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Enum_SameType_DirectAssignment()
    {
        const string source = """
            using SwiftMap;

            public enum Status { Active, Inactive }

            public class Src { public Status Status { get; set; } }
            public class Dst { public Status Status { get; set; } }

            [Mapper]
            public partial class AppMapper
            {
                public partial Dst Map(Src source);
            }
            """;

        var (diags, gen) = RunGenerator(source);

        Assert.Empty(diags.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("Status = source.Status", gen);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 6: enum → string
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Enum_ToStringConversion()
    {
        const string source = """
            using SwiftMap;

            public enum Status { Active }

            public class Src { public Status Status { get; set; } }
            public class Dst { public string Status { get; set; } = ""; }

            [Mapper]
            public partial class AppMapper
            {
                public partial Dst Map(Src source);
            }
            """;

        var (diags, gen) = RunGenerator(source);

        Assert.Empty(diags.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(".ToString()", gen);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 7: multiple methods in the same mapper class
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleMethodsInSameMapper_BothEmitted()
    {
        const string source = """
            using SwiftMap;

            public class ASrc  { public int X { get; set; } }
            public class ADst  { public int X { get; set; } }
            public class BSrc  { public string Y { get; set; } = ""; }
            public class BDst  { public string Y { get; set; } = ""; }

            [Mapper]
            public partial class AppMapper
            {
                public partial ADst MapA(ASrc source);
                public partial BDst MapB(BSrc source);
            }
            """;

        var (diags, gen) = RunGenerator(source);

        Assert.Empty(diags.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("MapA", gen);
        Assert.Contains("MapB", gen);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 8: non-partial class is ignored
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NonPartialClass_NotTriggered()
    {
        const string source = """
            using SwiftMap;

            public class Src { public int Id { get; set; } }
            public class Dst { public int Id { get; set; } }

            [Mapper]
            public class NotPartialMapper
            {
                public Dst Map(Src source) => new Dst { Id = source.Id };
            }
            """;

        var (_, gen) = RunGenerator(source);

        // Generator should not produce output for non-partial class
        Assert.DoesNotContain("partial class NotPartialMapper", gen);
    }
}
