using System.Reflection;

Console.WriteLine($"🌳 Weald v{GetVersion()} // Ctrl+C or .exit to quit");
Repl(line => Console.WriteLine($"echo {line}"));

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
        if ("exit".StartsWith(command) || "quit".StartsWith(command)) {
            return false;
        }

        if ("help".StartsWith(command)) {
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
