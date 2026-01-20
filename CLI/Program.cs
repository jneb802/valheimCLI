using System.Net.Sockets;
using System.Text;

namespace valheim_cli;

// UTF8 without BOM to avoid encoding issues
static class Enc
{
    public static readonly Encoding UTF8NoBOM = new UTF8Encoding(false);
}

class Program
{
    private static int _port = 5555;
    private static string _host = "127.0.0.1";

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--help")
        {
            PrintHelp();
            return 0;
        }

        // Parse port from args
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-p" || args[i] == "--port")
            {
                if (int.TryParse(args[i + 1], out var port))
                {
                    _port = port;
                }
            }
        }

        // Check if command was provided as argument
        var commandArgs = args.Where(a => !a.StartsWith("-") && a != args.FirstOrDefault(x => x == "-p" || x == "--port")?.Let(x => args[Array.IndexOf(args, x) + 1])).ToList();

        if (commandArgs.Count > 0)
        {
            // Single command mode
            var command = string.Join(" ", commandArgs);
            return ExecuteSingleCommand(command);
        }

        // Interactive mode
        return InteractiveMode();
    }

    static void PrintHelp()
    {
        Console.WriteLine("Valheim CLI - Remote console for Valheim");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  valheim-cli [options] [command]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --port <port>  Port to connect to (default: 5555)");
        Console.WriteLine("  --help             Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  valheim-cli                    Interactive mode");
        Console.WriteLine("  valheim-cli help               Run 'help' command");
        Console.WriteLine("  valheim-cli spawn Boar 5       Run 'spawn Boar 5' command");
        Console.WriteLine("  valheim-cli -p 5556 pos        Connect to port 5556, run 'pos'");
        Console.WriteLine();
        Console.WriteLine("Interactive commands:");
        Console.WriteLine("  exit, quit         Exit the CLI");
        Console.WriteLine("  commands           List all available Valheim commands");
        Console.WriteLine("  help               Show Valheim console help");
    }

    static int ExecuteSingleCommand(string command)
    {
        try
        {
            using var client = Connect();
            if (client == null) return 1;

            var response = SendCommand(client, command);
            foreach (var line in response)
            {
                Console.WriteLine(line);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static int InteractiveMode()
    {
        Console.WriteLine($"Valheim CLI - Connecting to {_host}:{_port}");
        Console.WriteLine("Type 'exit' to quit, 'commands' to list available commands");
        Console.WriteLine();

        TcpClient? client = null;

        while (true)
        {
            Console.Write("valheim> ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            // Remove any non-printable characters and trim (fixes Warp terminal issues)
            input = new string(input.Where(c => !char.IsControl(c) && c != '\u200B' && c != '\uFEFF').ToArray()).Trim();

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (input.Equals("reconnect", StringComparison.OrdinalIgnoreCase))
            {
                client?.Close();
                client = null;
                Console.WriteLine("Reconnecting...");
                continue;
            }

            try
            {
                // Connect if not connected
                if (client == null || !client.Connected)
                {
                    client?.Close();
                    client = Connect();
                    if (client == null)
                    {
                        Console.WriteLine("Failed to connect. Is Valheim running with the mod?");
                        continue;
                    }
                }

                if (input.Equals("commands", StringComparison.OrdinalIgnoreCase))
                {
                    var commands = ListCommands(client);
                    Console.WriteLine($"\nAvailable commands ({commands.Count}):\n");
                    foreach (var cmd in commands.OrderBy(c => c.Name))
                    {
                        var cheatMarker = cmd.IsCheat ? " [CHEAT]" : "";
                        Console.WriteLine($"  {cmd.Name,-20} {cmd.Description}{cheatMarker}");
                    }
                    Console.WriteLine();
                }
                else
                {
                    // Debug: show exactly what we're sending
                    Console.WriteLine($"[DEBUG] Sending command: '{input}' (length: {input.Length})");
                    Console.WriteLine($"[DEBUG] Bytes: {string.Join(" ", System.Text.Encoding.UTF8.GetBytes(input).Select(b => b.ToString("X2")))}");

                    var response = SendCommand(client, input);
                    foreach (var line in response)
                    {
                        Console.WriteLine(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                client?.Close();
                client = null;
            }
        }

        client?.Close();
        return 0;
    }

    static TcpClient? Connect()
    {
        try
        {
            var client = new TcpClient();
            client.Connect(_host, _port);

            var stream = client.GetStream();
            var reader = new StreamReader(stream, Enc.UTF8NoBOM);

            // Wait for ready message
            var ready = reader.ReadLine();
            if (ready != "VALHEIM_CLI_READY")
            {
                Console.Error.WriteLine($"Unexpected handshake: {ready}");
                client.Close();
                return null;
            }

            return client;
        }
        catch (SocketException)
        {
            Console.Error.WriteLine($"Cannot connect to Valheim at {_host}:{_port}");
            Console.Error.WriteLine("Make sure Valheim is running with the valheimCLI mod loaded.");
            return null;
        }
    }

    static List<string> SendCommand(TcpClient client, string command)
    {
        var stream = client.GetStream();
        var writer = new StreamWriter(stream, Enc.UTF8NoBOM) { AutoFlush = true };
        var reader = new StreamReader(stream, Enc.UTF8NoBOM);

        writer.WriteLine($"CMD:{command}");

        var result = new List<string>();
        var header = reader.ReadLine();

        if (header != null && header.StartsWith("OUTPUT:"))
        {
            if (int.TryParse(header.Substring(7), out var count))
            {
                for (int i = 0; i < count; i++)
                {
                    var line = reader.ReadLine();
                    if (line != null)
                        result.Add(line);
                }
            }

            // Read END_OUTPUT marker
            reader.ReadLine();
        }

        return result;
    }

    static List<CommandInfo> ListCommands(TcpClient client)
    {
        var stream = client.GetStream();
        var writer = new StreamWriter(stream, Enc.UTF8NoBOM) { AutoFlush = true };
        var reader = new StreamReader(stream, Enc.UTF8NoBOM);

        writer.WriteLine("LIST_COMMANDS");

        var result = new List<CommandInfo>();
        var header = reader.ReadLine();

        if (header != null && header.StartsWith("COMMANDS:"))
        {
            if (int.TryParse(header.Substring(9), out var count))
            {
                for (int i = 0; i < count; i++)
                {
                    var line = reader.ReadLine();
                    if (line != null)
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 2)
                        {
                            result.Add(new CommandInfo
                            {
                                Name = parts[0],
                                Description = parts[1],
                                IsCheat = parts.Length > 2 && parts[2] == "cheat"
                            });
                        }
                    }
                }
            }

            // Read END_COMMANDS marker
            reader.ReadLine();
        }

        return result;
    }

    record CommandInfo
    {
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public bool IsCheat { get; init; }
    }
}

static class Extensions
{
    public static TResult? Let<T, TResult>(this T? value, Func<T, TResult> func) where T : class
    {
        return value != null ? func(value) : default;
    }
}
