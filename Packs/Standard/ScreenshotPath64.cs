using System;
using System.IO;
using System.Text;

namespace valheimCLI
{
    // One command token even when a Windows account or run directory contains spaces.
    // A distinct command name keeps older CLI builds from treating the token as a bare
    // screenshot name and writing it into the user's normal Valheim save folder.
    internal static class ScreenshotPath64
    {
        public static bool TryDecode(string token, out string path)
        {
            path = "";
            if (token.Length is < 4 or > 16384 || token.IndexOfAny(new[] { '+', '/', '=' }) >= 0)
                return false;
            foreach (char c in token)
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return false;
            try
            {
                string base64 = token.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
                string decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(base64));
                if (decoded.IndexOf('\0') >= 0 || !Path.IsPathRooted(decoded) ||
                    !decoded.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
                path = Path.GetFullPath(decoded);
                return true;
            }
            catch (Exception error) when (error is FormatException or ArgumentException or DecoderFallbackException or NotSupportedException)
            { return false; }
        }
    }
}
