using System;
using System.IO;

namespace valheimCLI
{
    /// <summary>
    /// A connection's socket thread reads its caller's lines here. <see cref="Next"/> blocks for the next line and returns
    /// null once the caller has closed the connection or the server has stopped (Stop closes every client, which ends a
    /// blocked read), so the thread ends with its connection (#980). The old loop polled TcpClient.Connected, a stale
    /// last-operation flag, and kept one thread per finished command polling forever. A blocking read also serves several
    /// lines a caller sends in one write, which a DataAvailable poll left in the reader's buffer.
    /// </summary>
    internal sealed class ClientLines
    {
        private readonly TextReader _reader;

        internal ClientLines(TextReader reader) => _reader = reader ?? throw new ArgumentNullException(nameof(reader));

        /// <summary>The caller's next line, or null when it has closed the connection or the server stops.</summary>
        internal string? Next(Func<bool> running)
        {
            if (!running()) return null;
            try
            {
                string? line = _reader.ReadLine();
                return running() ? line : null;
            }
            catch (ObjectDisposedException) { return null; }
            catch (IOException) when (!running()) { return null; }
        }
    }
}
