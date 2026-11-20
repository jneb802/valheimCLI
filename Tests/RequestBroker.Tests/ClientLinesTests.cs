using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using valheimCLI;
using Xunit;

namespace valheimCLI.Tests;

/// <summary>#980: a connection's socket thread ends when its caller closes the connection, or when the server stops.</summary>
public sealed class ClientLinesTests
{
    [Fact]
    public void EveryConnectionThreadEndsWhenItsCallerCloses()
    {
        // The CommandServer shape: one thread per accepted connection reading lines until Next returns null.
        // Each caller sends one command, reads the reply and closes, as valheim-test does per command.
        const int Commands = 40;
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var threads = new List<Thread>();
        var accepted = new List<TcpClient>();
        try
        {
            for (int i = 0; i < Commands; i++)
            {
                using TcpClient caller = new TcpClient();
                caller.Connect(IPAddress.Loopback, port);
                TcpClient client = listener.AcceptTcpClient();
                accepted.Add(client);
                Thread thread = new Thread(() => Serve(client, () => true)) { IsBackground = true };
                thread.Start();
                threads.Add(thread);

                NetworkStream stream = caller.GetStream();
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                var reader = new StreamReader(stream, new UTF8Encoding(false));
                writer.WriteLine("PING");
                Assert.Equal("PONG", reader.ReadLine());
            }

            // The server keeps running: only the callers' close may end the threads.
            foreach (Thread thread in threads)
                Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "a connection thread kept running after its caller closed");
        }
        finally
        {
            foreach (TcpClient client in accepted) client.Close();
        }
    }

    [Fact]
    public void SeveralLinesInOneWriteAreEachRead()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using TcpClient caller = new TcpClient();
        caller.Connect(IPAddress.Loopback, port);
        using TcpClient accepted = listener.AcceptTcpClient();
        byte[] bytes = Encoding.UTF8.GetBytes("CMD:a\nCMD:b\n");
        caller.GetStream().Write(bytes, 0, bytes.Length);
        caller.Close();
        using var reader = new StreamReader(accepted.GetStream(), new UTF8Encoding(false));
        var lines = new ClientLines(reader);

        Assert.Equal("CMD:a", lines.Next(() => true));
        Assert.Equal("CMD:b", lines.Next(() => true));
        Assert.Null(lines.Next(() => true));
    }

    [Fact]
    public void ABlockedReadEndsWhenTheServerClosesTheConnection()
    {
        // Stop() closes every client; an idle caller's thread must end then too.
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using TcpClient caller = new TcpClient();
        caller.Connect(IPAddress.Loopback, port);
        TcpClient accepted = listener.AcceptTcpClient();
        bool running = true;
        string? result = "unset";
        var reader = new StreamReader(accepted.GetStream(), new UTF8Encoding(false));
        Thread thread = new Thread(() => result = new ClientLines(reader).Next(() => Volatile.Read(ref running))) { IsBackground = true };
        thread.Start();
        Assert.False(thread.Join(TimeSpan.FromMilliseconds(200)), "an open, silent connection should keep its thread waiting");

        Volatile.Write(ref running, false);
        accepted.Close();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "the read did not end when the server closed the connection");
        Assert.Null(result);
    }

    private static void Serve(TcpClient client, Func<bool> running)
    {
        using NetworkStream stream = client.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        var lines = new ClientLines(reader);
        try
        {
            string? line;
            while ((line = lines.Next(running)) != null)
                if (line == "PING") writer.WriteLine("PONG");
        }
        catch (IOException) { }
    }
}
