using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FiliusModemInterface.Filius;
using FiliusModemInterface.JavaObjectStream;
using static FiliusModemInterface.Program;

namespace FiliusModemInterface;

public class FiliusServer(IPAddress ip, int port)
{
    private const int _maxConnections = 20;
    private const int _idleProcessTimeout = 50;

    private readonly TcpListener _listener = new(ip, port);
    private readonly Dictionary<int, Client> _clients = new();
    private readonly Dictionary<int, Task> _handles = new();

    private readonly ConcurrentDictionary<string, int> _macTable = new();
    
    public async Task RunAsync(CancellationToken ct)
    {
        _listener.Start();
        try
        {
            LogInfo($"Startet broadcasting server. Listening on {_listener.LocalEndpoint}");
            while (!ct.IsCancellationRequested)
            {
                TcpClient newClient = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                if (_clients.Count >= _maxConnections)
                {
                    newClient.Close();
                    LogWarning($"Disposed incoming connection. Max parallel connections of {_maxConnections} reached!");
                    continue;
                }

                LogInfo($"New client connected {newClient.Client.RemoteEndPoint}");

                int id = _clients.Count + 1;
                Task handle = HandleClientAsync(id, newClient, ct);
                _handles.Add(id, handle);
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async Task HandleClientAsync(int id, TcpClient client, CancellationToken ct)
    {
        byte[] magicBytes = [0xAC, 0xED, 0x00, 0x05];
        
        await using NetworkStream stream = client.GetStream();
        await stream.WriteAsync(magicBytes, 0, magicBytes.Length, ct);
        
        var magicBytesCheck = new byte[4];
        await stream.ReadExactlyAsync(magicBytesCheck, 0, magicBytesCheck.Length, ct);
        if (!magicBytesCheck.SequenceEqual(new ReadOnlySpan<byte>(magicBytes)))
        {
            LogError($"Failed to receive magic bytes for client {id}. Disposing client.");
            client.Close();
            return;
        }
        
        long lastPosition = 0;
        
        var buffer = new byte[1024];
        using MemoryStream memory = new();
        using JavaObjectReader objectReader = new(memory);
        using JavaObjectWriter objectWriter = new(stream, JavaObjectSerializer.GetClassDesc);
        _clients.Add(id, new Client(client, stream, objectReader, objectWriter, new SemaphoreSlim(1, 1)));
        
        while (!ct.IsCancellationRequested && client.Connected)
        {
            try
            {
                while (true)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(_idleProcessTimeout);

                    var bytesRead = 0;
                    try
                    {
                        bytesRead = await stream.ReadAsync(buffer.AsMemory(), cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (bytesRead == 0)
                        break;
                    await memory.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                }

                if (memory.Position - lastPosition == 0)
                    continue;

                try
                {
                    var sw = Stopwatch.StartNew();
                    memory.Seek(lastPosition, SeekOrigin.Begin);

                    var frame = objectReader.ReadObject<EthernetFrame>();
                    _ = HandleFrameAsync(id, frame, ct).ConfigureAwait(false);

                    lastPosition = memory.Length;
                    
                    sw.Stop();
                    LogInfo($"Handling frame took: {sw.Elapsed}");
                }
                catch (EndOfStreamException)
                {
                    LogInfo("Rollback...");
                }
                finally
                {
                    memory.Seek(0, SeekOrigin.End);
                }
            }
            catch (IOException ioEx) when (ioEx.InnerException is SocketException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogError($"An error occured while reading client {id}: {ex}");
            }
        }
        
        LogError($"Client {id} disconnected");
        
        _clients.Remove(id);
        _handles.Remove(id);

        foreach (string index in _macTable.Where(kvp => kvp.Value == id).Select(kvp => kvp.Key))
            _macTable.Remove(index, out _);
    }

    private async Task HandleFrameAsync(int sourcePort, EthernetFrame frame, CancellationToken ct)
    {
        _macTable[frame.SourceMac] = sourcePort;

        int targetPort = _macTable.GetValueOrDefault(frame.DestinationMac, defaultValue: -1);
        if (targetPort != -1)
        {
            Client client = _clients[targetPort];
            try
            {
                await client.WriteLock.WaitAsync(ct);
                client.Writer.WriteObject(frame);
                await client.Stream.FlushAsync(ct);
            }
            catch (IOException)
            {
            } // In case the connection broke.
            finally
            {
                client.WriteLock.Release();
            }
        }
        else
        {
            Client[] targetWriters = _clients
                .Where(kvp => kvp.Key != sourcePort)
                .Select(kvp => kvp.Value)
                .ToArray();
            await Parallel.ForEachAsync(targetWriters, ct, async (client, innerCt) =>
            {
                try
                {
                    await client.WriteLock.WaitAsync(innerCt);
                    client.Writer.WriteObject(frame);
                    await client.Stream.FlushAsync(innerCt);
                }
                catch (IOException)
                {
                } // In case the connection broke.
                finally
                {
                    client.WriteLock.Release();
                }
            });
        }
    }

    private record Client(TcpClient TcpClient, NetworkStream Stream, JavaObjectReader Reader, JavaObjectWriter Writer, SemaphoreSlim WriteLock);
}