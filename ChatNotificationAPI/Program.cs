using Microsoft.AspNetCore.SignalR;
using System;
using System.Net;
using System.Net.Sockets;

var builder = WebApplication.CreateBuilder(args);

// ﬁ—«¡… «·≈⁄œ«œ« 
var preferredIP = builder.Configuration["ServerSettings:PreferredIP"];
var useAllInterfaces = builder.Configuration.GetValue<bool>("ServerSettings:UseAllInterfaces", false);
var signalRPort = builder.Configuration.GetValue<int>("ServerSettings:SignalRPort", 5000);
var apiPort = builder.Configuration.GetValue<int>("ServerSettings:ApiPort", 7001);

//  ÕœÌœ «·‹ IP «·„‰«”»
string bindIP;
if (useAllInterfaces)
{
    // «” „⁄ ⁄·Ï Ã„Ì⁄ «·‹ IPs
    bindIP = "0.0.0.0";
}
else
{
    //  Õﬁﬁ „‰ ÊÃÊœ «·‹ IP «·„›÷·
    var localIPs = GetLocalIPAddresses();
    if (localIPs.Contains(preferredIP))
    {
        bindIP = preferredIP;
    }
    else
    {
        Console.WriteLine($"Warning: IP {preferredIP} not found on this machine.");
        Console.WriteLine($"Available IPs: {string.Join(", ", localIPs)}");

        // «” Œœ„ √Ê· IP „ «Õ (€Ì— localhost)
        var availableIP = localIPs.FirstOrDefault(ip => ip != "127.0.0.1" && ip != "::1");
        bindIP = availableIP ?? "localhost";
        Console.WriteLine($"Using fallback IP: {bindIP}");
    }
}

Console.WriteLine($"Starting server on: http://{bindIP}:{apiPort}");
Console.WriteLine($"Also listening on: http://localhost:{apiPort}");

//  ﬂÊÌ‰ Kestrel
builder.WebHost.UseUrls($"http://{bindIP}:{apiPort}", $"http://localhost:{apiPort}");

// ≈÷«›… SignalR
builder.Services.AddSignalR();

// ≈÷«›… CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWPFClient", policy =>
    {
        policy.AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true); // ··«Œ »«— ›ﬁÿ
    });
});

var app = builder.Build();

// «” Œœ«„ CORS
app.UseCors("AllowWPFClient");

// Map SignalR Hub
app.MapHub<ChatHub>("/chatHub");

// ≈÷«›… ’›Õ… »”Ìÿ… ··«Œ »«—
app.MapGet("/", () => "SignalR Server is running!");

app.Run();

// Helper function
static List<string> GetLocalIPAddresses()
{
    var ips = new List<string>();
    var host = Dns.GetHostEntry(Dns.GetHostName());
    foreach (var ip in host.AddressList)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            ips.Add(ip.ToString());
        }
    }

    // √÷› localhost œ«∆„«
    if (!ips.Contains("127.0.0.1"))
        ips.Add("127.0.0.1");

    return ips;
}

// ChatHub Class
public class ChatHub : Hub
{

    public async Task SendMessageToUser(int fromUserId, int toUserId, string message)
    {
        await Clients.User(toUserId.ToString())
            .SendAsync("ReceiveMessage", fromUserId, toUserId, message, DateTime.Now);
    }

    public async Task SetUserIdentifier(string userId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, userId);
    }

    public override async Task OnConnectedAsync()
    {
        Console.WriteLine($"Client connected: {Context.ConnectionId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        Console.WriteLine($"Client disconnected: {Context.ConnectionId}");
        await base.OnDisconnectedAsync(exception);
    }
}