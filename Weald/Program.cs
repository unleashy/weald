using System.Reflection;
using Weald.Core;

Console.WriteLine($"Weald 🌳 v{GetVersion()} // Ctrl+C or .exit to quit");
Repl(line => {
    var source = Source.FromString("<repl>", line);
    var lexer = Lexer.Create(source);

    foreach (var token in lexer) {
        Console.WriteLine(token);
    }
});

return;

static string GetVersion()
{
    var version = Assembly.GetExecutingAssembly().GetName().Version!;
    return $"{version.Major}.{version.Minor}.{version.Revision}";
}

static void Repl(Action<string> action)
{
    while (true) {
        Console.Write("\n> ");
        var line = Console.ReadLine()?.Trim();
        if (line == null) break;

        if (line.StartsWith('.')) {
            if (HandleCommand(line[1..])) {
                continue;
            }
            else {
                break;
            }
        }

        action(line);
    }
}

static bool HandleCommand(string command)
{
    if (!string.IsNullOrWhiteSpace(command)) {
        if (
            "exit".StartsWith(command, StringComparison.Ordinal) ||
            "quit".StartsWith(command, StringComparison.Ordinal)
        ) {
            return false;
        }

        if ("help".StartsWith(command, StringComparison.Ordinal)) {
            Console.WriteLine("Available commands:");
            Console.WriteLine("  .exit   Quit the interactive environment (alias .quit)");
            Console.WriteLine("  .help   Display this help text");
            Console.WriteLine("You may also use abbreviations, for example, .h for .help");

            return true;
        }
    }

    Console.Error.WriteLine($"REPL error: unknown command .{command}");
    Console.Error.WriteLine("Type .help to see a list of commands");

    return true;
}
