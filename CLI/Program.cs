using valheim_cli.Testing;

namespace valheim_cli;

class Program
{
    private static int _port = ConnectionDefaults.Port;
    private static string _host = ConnectionDefaults.Host;
    private static bool _verbose = false;
    private static string? _testFile = null;
    private static bool _launch = false;
    private static bool _stopAfter = false;
    private static bool _status = false;
    private static string? _gamePath = null;
    private static Dictionary<string, string> _variables = new();

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--help")
        {
            PrintHelp();
            return 0;
        }

        // Parse arguments
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "-p" || args[i] == "--port") && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int port))
                {
                    _port = port;
                }
                i++; // Skip next arg
            }
            else if ((args[i] == "-t" || args[i] == "--test") && i + 1 < args.Length)
            {
                _testFile = args[i + 1];
                i++; // Skip next arg
            }
            else if (args[i] == "-v" || args[i] == "--verbose")
            {
                _verbose = true;
            }
            else if (args[i] == "--launch" || args[i] == "-l")
            {
                _launch = true;
            }
            else if (args[i] == "--stop-after")
            {
                _stopAfter = true;
            }
            else if (args[i] == "--status")
            {
                _status = true;
            }
            else if (args[i] == "--game-path" && i + 1 < args.Length)
            {
                _gamePath = args[i + 1];
                i++; // Skip next arg
            }
            else if (args[i] == "--var" && i + 1 < args.Length)
            {
                string varArg = args[i + 1];
                int eqIndex = varArg.IndexOf('=');
                if (eqIndex > 0)
                {
                    string key = varArg.Substring(0, eqIndex);
                    string value = varArg.Substring(eqIndex + 1);
                    _variables[key] = value;
                }
                i++; // Skip next arg
            }
        }

        // Status mode - check game status
        if (_status)
        {
            return ShowStatus();
        }

        // Test mode
        if (_testFile != null)
        {
            return RunTestMode(_testFile).GetAwaiter().GetResult();
        }

        // Check if command was provided as argument (exclude flags and their values)
        List<string> commandArgs = new();
        for (int i = 0; i < args.Length; i++)
        {
            // Flags with values
            if (args[i] == "-p" || args[i] == "--port" || args[i] == "-t" || args[i] == "--test" || args[i] == "--game-path" || args[i] == "--var")
            {
                i++; // Skip the value too
                continue;
            }
            // Boolean flags
            if (args[i] == "-v" || args[i] == "--verbose" || args[i] == "--launch" || args[i] == "-l" ||
                args[i] == "--stop-after" || args[i] == "--status")
            {
                continue;
            }
            if (!args[i].StartsWith("-"))
            {
                commandArgs.Add(args[i]);
            }
        }

        if (commandArgs.Count > 0)
        {
            // Single command mode
            string command = string.Join(" ", commandArgs);
            return ExecuteSingleCommand(command);
        }

        // Interactive mode
        return InteractiveMode();
    }

    static int ShowStatus()
    {
        GameLauncher launcher = new GameLauncher(_gamePath, _host, _port);
        GameStatus status = launcher.GetStatus();

        Console.WriteLine("Valheim CLI Status");
        Console.WriteLine("==================");
        Console.WriteLine($"Game Path:   {status.GamePath}");
        Console.WriteLine($"Game:        {(status.IsRunning ? "Running" : "Not running")}");
        Console.WriteLine($"Server:      {status.Host}:{status.Port}");
        Console.WriteLine($"Connection:  {(status.IsConnected ? "Connected" : "Not connected")}");

        return status.IsConnected ? 0 : 1;
    }

    static async Task<int> RunTestMode(string testFile)
    {
        // Support glob patterns
        List<string> testFiles = new();

        if (testFile.Contains('*'))
        {
            string directory = Path.GetDirectoryName(testFile) ?? ".";
            string pattern = Path.GetFileName(testFile);
            testFiles.AddRange(Directory.GetFiles(directory, pattern));
        }
        else if (File.Exists(testFile))
        {
            testFiles.Add(testFile);
        }
        else
        {
            Console.Error.WriteLine($"Test file not found: {testFile}");
            return 1;
        }

        if (testFiles.Count == 0)
        {
            Console.Error.WriteLine($"No test files found matching: {testFile}");
            return 1;
        }

        Console.WriteLine("Valheim CLI Test Runner");
        Console.WriteLine("=======================");

        // Create game launcher
        GameLauncher launcher = new GameLauncher(_gamePath, _host, _port);

        // Create runner with options (CLI flags will override YAML settings)
        TestRunnerOptions options = new TestRunnerOptions
        {
            Verbose = _verbose,
            Launch = _launch ? true : null,      // Only set if flag was provided
            StopAfter = _stopAfter ? true : null, // Only set if flag was provided
            Variables = _variables
        };

        TestRunner runner = new TestRunner(launcher, options, _host, _port);

        int totalFailed = 0;
        foreach (string file in testFiles)
        {
            TestPlanResult result = await runner.RunTestFileAsync(file);
            totalFailed += result.Failed + result.Errors;
        }

        return totalFailed > 0 ? 1 : 0;
    }

    static void PrintHelp()
    {
        Console.WriteLine("Valheim CLI - Remote console for Valheim");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  valheim-cli [options] [command]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --port <port>     Port to connect to (default: 5555)");
        Console.WriteLine("  -t, --test <file>     Run a YAML test file");
        Console.WriteLine("  -v, --verbose         Verbose output (for test mode)");
        Console.WriteLine("  -l, --launch          Launch Valheim before running tests");
        Console.WriteLine("  --stop-after          Stop game after tests (only if all pass)");
        Console.WriteLine("  --status              Check game and connection status");
        Console.WriteLine("  --game-path <path>    Path to Valheim installation");
        Console.WriteLine("  --var <key=value>     Set a test variable (can be used multiple times)");
        Console.WriteLine("  --help                Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  valheim-cli                              Interactive mode");
        Console.WriteLine("  valheim-cli help                         Run 'help' command");
        Console.WriteLine("  valheim-cli spawn Boar 5                 Run 'spawn Boar 5' command");
        Console.WriteLine("  valheim-cli -p 5556 pos                  Connect to port 5556, run 'pos'");
        Console.WriteLine("  valheim-cli --test tests/spawn.yaml      Run test file");
        Console.WriteLine("  valheim-cli -t tests/*.yaml -v           Run all tests verbosely");
        Console.WriteLine("  valheim-cli --status                     Check if game is running");
        Console.WriteLine("  valheim-cli -t tests/spawn.yaml --launch Launch game, then run tests");
        Console.WriteLine("  valheim-cli -t tests/road.yaml --var n=1 Pass variable to test");
        Console.WriteLine("  valheim-cli -t tests/spawn.yaml --launch --stop-after");
        Console.WriteLine("                                           Launch, test, stop if passed");
        Console.WriteLine();
        Console.WriteLine("Interactive commands:");
        Console.WriteLine("  exit, quit         Exit the CLI");
        Console.WriteLine("  commands           List all available Valheim commands");
        Console.WriteLine("  help               Show Valheim console help");
        Console.WriteLine();
        Console.WriteLine("Test File Format (YAML):");
        Console.WriteLine("  See documentation for full test file schema including");
        Console.WriteLine("  game: section (launch, launchTimeout, stopAfter),");
        Console.WriteLine("  waitFor conditions, expect assertions, and variables.");
        Console.WriteLine();
        Console.WriteLine("Game Path Detection (priority order):");
        Console.WriteLine("  1. --game-path argument");
        Console.WriteLine("  2. VALHEIM_PATH environment variable");
        Console.WriteLine("  3. Default: ~/Library/Application Support/Steam/steamapps/common/Valheim/");
    }

    static int ExecuteSingleCommand(string command)
    {
        try
        {
            using ValheimClient client = new ValheimClient(_host, _port);
            if (!client.Connect())
            {
                Console.Error.WriteLine($"Cannot connect to Valheim at {_host}:{_port}");
                Console.Error.WriteLine("Make sure Valheim is running with the valheimCLI mod loaded.");
                return 1;
            }

            List<string> response = client.SendCommand(command);
            foreach (string line in response)
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

        ValheimClient? client = null;

        while (true)
        {
            Console.Write("valheim> ");
            string? input = Console.ReadLine();

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
                client?.Dispose();
                client = null;
                Console.WriteLine("Reconnecting...");
                continue;
            }

            try
            {
                // Connect if not connected
                if (client == null || !client.IsConnected)
                {
                    client?.Dispose();
                    client = new ValheimClient(_host, _port);
                    if (!client.Connect())
                    {
                        Console.WriteLine("Failed to connect. Is Valheim running with the mod?");
                        client.Dispose();
                        client = null;
                        continue;
                    }
                }

                if (input.Equals("commands", StringComparison.OrdinalIgnoreCase))
                {
                    List<CommandInfo> commands = client.ListCommands();
                    Console.WriteLine($"\nAvailable commands ({commands.Count}):\n");
                    foreach (CommandInfo cmd in commands.OrderBy(c => c.Name))
                    {
                        string cheatMarker = cmd.IsCheat ? " [CHEAT]" : "";
                        Console.WriteLine($"  {cmd.Name,-20} {cmd.Description}{cheatMarker}");
                    }
                    Console.WriteLine();
                }
                else
                {
                    List<string> response = client.SendCommand(input);
                    foreach (string line in response)
                    {
                        Console.WriteLine(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                client?.Dispose();
                client = null;
            }
        }

        client?.Dispose();
        return 0;
    }

}
