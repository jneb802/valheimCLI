using System.Diagnostics;

namespace valheim_cli.Testing;

public class GameLauncher
{
    private readonly string _gamePath;
    private readonly string _host;
    private readonly int _port;
    private Process? _gameProcess;

    public GameLauncher(string? gamePath = null, string host = ConnectionDefaults.Host, int port = ConnectionDefaults.Port)
    {
        _gamePath = ResolveGamePath(gamePath);
        _host = host;
        _port = port;
    }

    public string GamePath => _gamePath;

    /// <summary>
    /// Resolves game path with priority: explicit path > VALHEIM_PATH env > default
    /// </summary>
    private static string ResolveGamePath(string? explicitPath)
    {
        // Priority 1: Explicit path argument
        if (!string.IsNullOrEmpty(explicitPath))
        {
            return explicitPath;
        }

        // Priority 2: Environment variable
        string? envPath = Environment.GetEnvironmentVariable("VALHEIM_PATH");
        if (!string.IsNullOrEmpty(envPath))
        {
            return envPath;
        }

        // Priority 3: Default macOS Steam path
        string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDir, "Library", "Application Support", "Steam", "steamapps", "common", "Valheim");
    }

    /// <summary>
    /// Check if Valheim process is currently running
    /// </summary>
    public bool IsGameRunning()
    {
        // Check both cases (case-sensitive on macOS)
        return ProcessExists("valheim") || ProcessExists("Valheim");
    }

    /// <summary>
    /// Check if a process exists by name, optionally performing an action on each process
    /// </summary>
    private static bool ProcessExists(string name, Action<Process>? action = null)
    {
        Process[] processes = Process.GetProcessesByName(name);
        bool found = processes.Length > 0;
        foreach (Process p in processes)
        {
            try
            {
                action?.Invoke(p);
            }
            catch
            {
                // Process may have already exited
            }
            finally
            {
                p.Dispose();
            }
        }
        return found;
    }

    /// <summary>
    /// Try to connect to the game's TCP server
    /// </summary>
    public bool TryConnect()
    {
        using ValheimClient client = new ValheimClient(_host, _port);
        return client.Connect();
    }

    /// <summary>
    /// Launch Valheim using run_bepinex.sh
    /// </summary>
    public bool LaunchGame()
    {
        string scriptPath = Path.Combine(_gamePath, "run_bepinex.sh");

        if (!File.Exists(scriptPath))
        {
            Console.Error.WriteLine($"Launch script not found: {scriptPath}");
            return false;
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{scriptPath}\"",
                WorkingDirectory = _gamePath,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            _gameProcess = Process.Start(startInfo);
            return _gameProcess != null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to launch game: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Wait for the game to be ready (TCP server accepting connections)
    /// </summary>
    public async Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        DateTime deadline = DateTime.Now.Add(timeout);
        TimeSpan pollInterval = TimeSpan.FromSeconds(2);

        while (DateTime.Now < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (TryConnect())
            {
                return true;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Stop the game process gracefully
    /// </summary>
    public void StopGame()
    {
        // First try to kill the tracked process
        if (_gameProcess != null && !_gameProcess.HasExited)
        {
            try
            {
                _gameProcess.Kill(entireProcessTree: true);
                _gameProcess.Dispose();
                _gameProcess = null;
                return;
            }
            catch
            {
                // Fall through to find by name
            }
        }

        // Find and kill any Valheim processes (check both cases for macOS)
        Action<Process> killAction = p => p.Kill(entireProcessTree: true);
        ProcessExists("valheim", killAction);
        ProcessExists("Valheim", killAction);
    }

    /// <summary>
    /// Get the current status of the game
    /// </summary>
    public GameStatus GetStatus()
    {
        bool running = IsGameRunning();
        bool connected = TryConnect();

        return new GameStatus
        {
            IsRunning = running,
            IsConnected = connected,
            GamePath = _gamePath,
            Host = _host,
            Port = _port
        };
    }
}

public class GameStatus
{
    public bool IsRunning { get; set; }
    public bool IsConnected { get; set; }
    public string GamePath { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }

    public override string ToString()
    {
        string runningStatus = IsRunning ? "Running" : "Not running";
        string connectionStatus = IsConnected ? "Connected" : "Not connected";
        return $"Game: {runningStatus}, Server: {connectionStatus} ({Host}:{Port})";
    }
}
