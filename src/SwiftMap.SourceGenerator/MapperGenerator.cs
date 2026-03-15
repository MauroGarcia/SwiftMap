using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SwiftMap.SourceGenerator.Emit;
using SwiftMap.SourceGenerator.Pipeline;

namespace SwiftMap.SourceGenerator;

/// <summary>
/// Roslyn IIncrementalGenerator that implements Mapperly-style compile-time mapping
/// for classes decorated with <c>[SwiftMap.Mapper]</c>.
/// </summary>
[Generator]
public sealed class MapperGenerator : IIncrementalGenerator
{
    private const string MapperFqn = "SwiftMap.MapperAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                MapperFqn,
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax cls
                    && cls.Modifiers.Any(SyntaxKind.PartialKeyword),
                transform: static (ctx, ct) =>
                    MapperModelExtractor.Extract(ctx, ct))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(models, static (ctx, model) =>
        {
            var src = MappingBodyEmitter.Emit(model!);
            ctx.AddSource($"{model!.ClassName}.g.cs",
                Microsoft.CodeAnalysis.Text.SourceText.From(src,
                    System.Text.Encoding.UTF8));
        });
    }
}
