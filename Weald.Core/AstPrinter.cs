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
        var (name, props) = GetPrinterData(ast);

        AppendLine(indent, $"@ {name} (location: {ast.Loc})");

        for (var i = 0; i < props.Length; ++i) {
            var (propName, value) = props[i];
            var isLast = i == props.Length - 1;

            PrintProperty(propName, value, isLast, indent);
        }
    }

    private void PrintProperty(string name, object? value, bool isLast, string indent)
    {
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
                AppendLine(
                    indent,
                    string.Create(CultureInfo.InvariantCulture, $"{pointer}{name}: {value}")
                );
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

    private static (string Name, (string Name, object? Value)[] Props) GetPrinterData(IAst ast)
    {
        var ty = ast.GetType();

        var name = ty.Name.StartsWith("Ast", StringComparison.Ordinal) ? ty.Name[3 ..] : ty.Name;

        var props = ty
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "Loc")
            .Select(p => (p.Name, p.GetValue(ast)))
            .OrderBy(p => p.Name, ByNameWithLocsAtEnd)
            .ToArray();

        return (name, props);
    }

    private static readonly Comparer<string> ByNameWithLocsAtEnd =
        Comparer<string>.Create((a, b) => {
            var aIsLoc = a.EndsWith("Loc", StringComparison.Ordinal);
            var bIsLoc = b.EndsWith("Loc", StringComparison.Ordinal);

            if (!(aIsLoc && bIsLoc)) {
                if (aIsLoc) return +1;
                if (bIsLoc) return -1;
            }

            return a.CompareTo(b, StringComparison.Ordinal);
        });
}
