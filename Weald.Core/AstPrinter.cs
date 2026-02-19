using System.Globalization;
using System.Reflection;
using System.Text;

namespace Weald.Core;

public sealed class AstPrinter
{
    public static string Print(IAst ast)
    {
        var printer = new AstPrinter();
        printer.Visit(ast, "");
        return printer.Finish().Trim();
    }

    private StringBuilder _s = new();
    private string? _indentOverride;

    private AstPrinter() {}

    private void Visit(IAst ast, string indent)
    {
        var nodeTy = ast.GetType();

        AppendLine(indent, $"@ {nodeTy.Name} (location: {ast.Loc})");

        var properties =
            nodeTy
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name != "Loc")
                .ToArray();

        for (var i = 0; i < properties.Length; ++i) {
            var property = properties[i];
            var isLast = i == properties.Length - 1;
            var value = property.GetValue(ast);

            PrintProperty(property, value, isLast, indent);
        }
    }

    private void PrintProperty(PropertyInfo property, object? value, bool isLast, string indent)
    {
        var name = property.Name;

        var pointer = isLast ? "└── " : "├── ";
        var continuation = isLast ? "    " : "│   ";

        switch (value) {
            case null:     AppendLine(indent, $"{pointer}{name}: ∅"); break;
            case string s: AppendLine(indent, $"{pointer}{name}: {s.Escape()}"); break;
            case Loc loc:  AppendLine(indent, $"{pointer}{name}: {loc}"); break;

            case IAst child: {
                AppendLine(indent, $"{pointer}{name}: ");
                Visit(child, indent + continuation);
                break;
            }

            case IEnumerable<IAst> children: {
                PrintChildren(indent, children, name, pointer, continuation);
                break;
            }

            default: {
                AppendLine(indent, $"{pointer}{name}: {value}");
                break;
            }
        }
    }

    private void PrintChildren(
        string indent,
        IEnumerable<IAst> children,
        string name,
        string pointer,
        string continuation
    )
    {
        if (children.TryGetNonEnumeratedCount(out var count)) {
            AppendLine(indent, $"{pointer}{name}: (count: {count})");
        }
        else {
            AppendLine(indent, $"{pointer}{name}:");
        }

        var childrenArray = children.ToArray();
        for (var i = 0; i < childrenArray.Length; ++i) {
            var child = childrenArray[i];
            var isLast = i == childrenArray.Length - 1;

            var childPointer = isLast ? "└── " : "├── ";
            var childContinuation = isLast ? "    " : "│   ";

            _indentOverride = indent + continuation + childPointer;
            Visit(child, indent + continuation + childContinuation);
        }
    }

    private void AppendLine(string indent, string s)
    {
        var effectiveIndent = _indentOverride ?? indent;
        _s.AppendLine(CultureInfo.InvariantCulture, $"{effectiveIndent}{s}");
        _indentOverride = null;
    }

    private string Finish() => _s.ToString();
}
