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
    //  Œ“Ì‰ „⁄—›«  «·„” Œœ„Ì‰ («Œ Ì«—Ì)
    private static readonly Dictionary<string, string> _userConnections = new();

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        System.Diagnostics.Debug.WriteLine($"Client connected: {connectionId}");

        // „Õ«Ê·… «·Õ’Ê· ⁄·Ï „⁄—› «·„” Œœ„ „‰ «·‹ Query String
        var httpContext = Context.GetHttpContext();
        var userId = httpContext?.Request.Query["userId"].ToString();

        if (!string.IsNullOrEmpty(userId))
        {
            _userConnections[userId] = connectionId;
            await Groups.AddToGroupAsync(connectionId, userId);
            System.Diagnostics.Debug.WriteLine($"User {userId} added to group");
        }

        await base.OnConnectedAsync();
    }

    public async Task SetUserIdentifier(string userId)
    {
        var connectionId = Context.ConnectionId;
        System.Diagnostics.Debug.WriteLine($"SetUserIdentifier called: UserId={userId}, ConnectionId={connectionId}");

        _userConnections[userId] = connectionId;
        await Groups.AddToGroupAsync(connectionId, userId);

        System.Diagnostics.Debug.WriteLine($"User {userId} added to group successfully");
    }

    public async Task SendMessageToUser(int fromUserId, int toUserId, string message)
    {
        System.Diagnostics.Debug.WriteLine($"SendMessageToUser: From={fromUserId}, To={toUserId}, Msg={message}");

        // „Õ«Ê·… «·≈—”«· ··„” Œœ„ «·„Õœœ
        try
        {
            // ÿ—Ìﬁ… 1: «” Œœ«„ Groups
            await Clients.Group(toUserId.ToString())
                .SendAsync("ReceiveMessage", fromUserId, toUserId, message, DateTime.Now);

            // ÿ—Ìﬁ… 2: «” Œœ«„ User (≈–« ﬂ«‰ ·œÌﬂ Authentication)
            // await Clients.User(toUserId.ToString())
            //     .SendAsync("ReceiveMessage", fromUserId, toUserId, message, DateTime.Now);

            // ÿ—Ìﬁ… 3: «” Œœ«„ Client (≈–« ﬂ‰   ⁄—› ConnectionId)
            // if (_userConnections.TryGetValue(toUserId.ToString(), out var connectionId))
            // {
            //     await Clients.Client(connectionId)
            //         .SendAsync("ReceiveMessage", fromUserId, toUserId, message, DateTime.Now);
            // }

            System.Diagnostics.Debug.WriteLine($"Message sent to group {toUserId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending message: {ex.Message}");
        }
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var connectionId = Context.ConnectionId;
        System.Diagnostics.Debug.WriteLine($"Client disconnected: {connectionId}");

        // ≈“«·… «·„” Œœ„ „‰ «·ﬁ«„Ê”
        var user = _userConnections.FirstOrDefault(x => x.Value == connectionId);
        if (!string.IsNullOrEmpty(user.Key))
        {
            _userConnections.Remove(user.Key);
        }

        await base.OnDisconnectedAsync(exception);
    }
}