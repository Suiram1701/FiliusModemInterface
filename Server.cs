using System.Net;
using System.Net.Sockets;
using FiliusModemInterface.Filius;
using FiliusModemInterface.JavaObjectStream;
using static FiliusModemInterface.Program;

namespace FiliusModemInterface;

public class Server(IPAddress ip, int port)
{
    private const int _maxConnections = 20;
    private const int _idleProcessTimeout = 50;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TcpListener _listener = new(ip, port);
    private readonly Dictionary<int, TcpClient> _clients = new();
    
    public async Task RunAsync(CancellationToken ct)
    {
        _listener.Start();
        try
        {
            LogInfo($"Startet broadcasting server. Listening on {_listener.LocalEndpoint}");
            while (!ct.IsCancellationRequested)
            {
                TcpClient newClient = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                try
                {
                    await _lock.WaitAsync(ct);
                    if (_clients.Count >= _maxConnections)
                    {
                        newClient.Close();
                        LogWarning($"Disposed incoming connection. Max parallel connections of {_maxConnections} reached!");
                        continue;
                    }

                    _clients.Add(_clients.Count, newClient);
                    LogInfo($"New client connected {newClient.Client.RemoteEndPoint}");
                    _ = HandleClientAsync(_clients.Count - 1, newClient, ct);
                }
                finally
                {
                    _lock.Release();
                }
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
        
        stream.ReadTimeout = _idleProcessTimeout;

        long lastPosition = 0;
        
        var buffer = new byte[1024];
        using MemoryStream memory = new();
        using JavaObjectReader reader = new(memory);
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
                    { bytesRead = await stream.ReadAsync(buffer.AsMemory(), cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException)
                    { break; }

                    if (bytesRead == 0)
                        return;
                    await memory.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                }
                
                if (memory.Position - lastPosition == 0)
                    continue;

                try
                {
                    memory.Seek(lastPosition, SeekOrigin.Begin);
                    var frame = JavaObjectSerializer.DeserializeObject<EthernetFrame>(reader.ReadObject());
                    LogInfo($"Read frame from client {id}: {frame}");
                    
                    lastPosition = memory.Length;
                }
                catch (EndOfStreamException) { }     // Do nothing
                finally 
                {
                    memory.Seek(0, SeekOrigin.End);
                }
            }
            catch (Exception ex)
            {
                LogError($"An error occured while reading client {id}: {ex}");
                continue;
            }
        }
    }
}