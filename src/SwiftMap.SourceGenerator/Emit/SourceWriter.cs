using System.Text;

namespace SwiftMap.SourceGenerator.Emit;

/// <summary>Lightweight indented string builder for source generation.</summary>
internal sealed class SourceWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;
    private const string IndentUnit = "    ";

    public void AppendLine(string line)
    {
        for (int i = 0; i < _indent; i++) _sb.Append(IndentUnit);
        _sb.AppendLine(line);
    }

    public void AppendLine() => _sb.AppendLine();

    public void OpenBrace()
    {
        AppendLine("{");
        _indent++;
    }

    public void CloseBrace(bool semicolon = false)
    {
        _indent--;
        AppendLine(semicolon ? "};" : "}");
    }

    public void Indent() => _indent++;
    public void Dedent() => _indent--;

    public override string ToString() => _sb.ToString();
}
