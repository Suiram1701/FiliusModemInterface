using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace FiliusModemInterface;

public partial class Program
{
    [GeneratedRegex(@"^([0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}$")]
    private static partial Regex MacValidatorRegEx();
    
    private static readonly Option<IPAddress> _bindOption = new("--bind", "-b")
    {
        Description = "The IP the server should listen on.",
        Required = true,
        DefaultValueFactory = _ => IPAddress.Loopback,
        Recursive = true
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
        },
        Recursive = true
    };

    private static readonly Option<string> _macOption = new("--mac", "-m")
    {
        Description = "The MAC address the NAT-Gateway will be reachable. By default it will be a random MAC.",
        Required = true,
        DefaultValueFactory = _ =>
        {
            var bytes = new byte[6];
            Random.Shared.NextBytes(bytes);
            return string.Join(":", bytes.Select(b => b.ToString("X2")));
        },
        Validators =
        {
            result =>
            {
                if (!MacValidatorRegEx().IsMatch(result.GetValueOrDefault<string>()))
                    result.AddError("MAC address is not valid.");
            }
        }
    };
    
    private static readonly Option<IPAddress> _ipOption = new("--ip", "-i")
    {
        Description = "The IP Address the gateway will be available under.",
        Required = true,
        DefaultValueFactory = _ => IPAddress.Parse("192.168.0.1"),
        Validators =
        {
            result =>
            {
                var ip = result.GetValueOrDefault<IPAddress>();
                if (ip.AddressFamily != AddressFamily.InterNetwork)
                    result.AddError("IP address is not an IPv4 address.");
                if (Equals(ip, IPAddress.Any) || Equals(ip, IPAddress.Loopback) || Equals(ip, IPAddress.Broadcast))
                    result.AddError("IP Address must not be any, loopback or broadcast address.");
            }
        }
    };
    
    private static Task<int> Main(string[] args)
    {
        args = ["nat", "--mac", "0E:5F:BB:BD:F2:AC"];
// #if DEBUG
//         Console.Write("args: ");
//         string str = Console.ReadLine() ?? "";
//         args = str.Split(' ');
// #endif
        
        RootCommand rootCommand = new("Starts a server where one or more Filius instances can connect to. It will act as a switch between these instances.")
        {
            _bindOption,
            _portOption
        };
        rootCommand.SetAction((parseResult, ct) =>
        {
            IPAddress bindIp = parseResult.GetRequiredValue(_bindOption);
            int bindPort = parseResult.GetRequiredValue(_portOption);
            FiliusServer filiusServer = new(bindIp, bindPort);
            
            return filiusServer.RunAsync(ct);
        });

        Command natCommand = new("nat","Start the server with the switch behaviour, but also setups a NIC in filius where requests will be routed into the internet.")
        {
            _macOption,
            _ipOption
        };
        natCommand.SetAction((parseResult, ct) =>
        {
            IPAddress bindIp = parseResult.GetRequiredValue(_bindOption);
            int bindPort = parseResult.GetRequiredValue(_portOption);
            FiliusServer filiusServer = new(bindIp, bindPort);
            
            string mac = parseResult.GetRequiredValue(_macOption);
            IPAddress ip = parseResult.GetRequiredValue(_ipOption);
            NatHandler nat = new();
            filiusServer.RespondOn(mac, ip, nat.HandleFrameAsync);
            
            return filiusServer.RunAsync(ct);
        });
        rootCommand.Add(natCommand);
        
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