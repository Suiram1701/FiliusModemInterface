using System.CommandLine;
using System.Net;

namespace FiliusModemInterface;

public class Program
{
    private static readonly Option<IPAddress> _bindOption = new("--bind", "-b")
    {
        Description = "The IP the server should listen on.",
        Required = true,
        DefaultValueFactory = _ => IPAddress.Loopback
    };
    
    private static readonly Option<int> _portOption = new("--port", "-p")
    {
        Description = "The port the server should listen on.",
        Required = true,
        DefaultValueFactory = _ => 12345,
        Validators =
        {
            result =>
            {
                var port = result.GetValueOrDefault<int>();
                if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
                    result.AddError($"The port has to be between {IPEndPoint.MinPort} and {IPEndPoint.MaxPort}.");
            }
        }
    };
    
    private static Task<int> Main(string[] args)
    {
#if DEBUG
        Console.Write("args: ");
        string str = Console.ReadLine() ?? "";
        args = str.Split(' ');
#endif
        
        RootCommand rootCommand = new("Starts a where one or more Filius instances can connect to. Can be used to connect more than two Filius instances together.")
        {
            _bindOption,
            _portOption
        };
        rootCommand.SetAction((parseResult, ct) =>
        {
            IPAddress ip = parseResult.GetRequiredValue(_bindOption);
            int port = parseResult.GetRequiredValue(_portOption);
            
            FiliusServer filiusServer = new(ip, port);
            return filiusServer.RunAsync(ct);
        });
        
        return rootCommand.Parse(args).InvokeAsync();
    }

    public static void LogInfo(string message)
    {
        Console.ResetColor();
        Console.WriteLine($"[{DateTime.Now}]: {message}");
    }
    
    public static void LogWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{DateTime.Now}]: {message}");
    }

    public static void LogError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{DateTime.Now}]: {message}");
    }
}