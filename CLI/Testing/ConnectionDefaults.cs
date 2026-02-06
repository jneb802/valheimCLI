using System.Text;

namespace valheim_cli.Testing;

public static class ConnectionDefaults
{
    public const string Host = "127.0.0.1";
    public const int Port = 5555;
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
}
