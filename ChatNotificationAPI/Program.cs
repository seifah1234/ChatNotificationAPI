using Microsoft.AspNetCore.SignalR;
using System;
using System.Net;
using System.Net.Sockets;

var builder = WebApplication.CreateBuilder(args);

// قراءة الإعدادات
var preferredIP = builder.Configuration["ServerSettings:PreferredIP"];
var useAllInterfaces = builder.Configuration.GetValue<bool>("ServerSettings:UseAllInterfaces", false);
var signalRPort = builder.Configuration.GetValue<int>("ServerSettings:SignalRPort", 5000);
var apiPort = builder.Configuration.GetValue<int>("ServerSettings:ApiPort", 7001);

// تحديد الـ IP المناسب
string bindIP;
if (useAllInterfaces)
{
    // استمع على جميع الـ IPs
    bindIP = "0.0.0.0";
}
else
{
    // تحقق من وجود الـ IP المفضل
    var localIPs = GetLocalIPAddresses();
    if (localIPs.Contains(preferredIP))
    {
        bindIP = preferredIP;
    }
    else
    {
        Console.WriteLine($"Warning: IP {preferredIP} not found on this machine.");
        Console.WriteLine($"Available IPs: {string.Join(", ", localIPs)}");

        // استخدم أول IP متاح (غير localhost)
        var availableIP = localIPs.FirstOrDefault(ip => ip != "127.0.0.1" && ip != "::1");
        bindIP = availableIP ?? "localhost";
        Console.WriteLine($"Using fallback IP: {bindIP}");
    }
}

Console.WriteLine($"Starting server on: http://{bindIP}:{apiPort}");
Console.WriteLine($"Also listening on: http://localhost:{apiPort}");

// تكوين Kestrel
// تأكد من هذا السطر
builder.WebHost.UseUrls("http://0.0.0.0:7001", "http://localhost:7001");
// إضافة SignalR
builder.Services.AddSignalR();

// إضافة CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWPFClient", policy =>
    {
        policy.AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true); // للاختبار فقط
    });
});

var app = builder.Build();

// استخدام CORS
app.UseCors("AllowWPFClient");

// Map SignalR Hub
app.MapHub<ChatHub>("/chatHub");

// إضافة صفحة بسيطة للاختبار
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

    // أضف localhost دائماً
    if (!ips.Contains("127.0.0.1"))
        ips.Add("127.0.0.1");

    return ips;
}

// ChatHub Class
// ChatHub Class - التصحيح
public class ChatHub : Hub
{
    private static readonly Dictionary<string, string> _userConnections = new();

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        Console.WriteLine($"Client connected: {connectionId}");

        var httpContext = Context.GetHttpContext();
        var userId = httpContext?.Request.Query["userId"].ToString();

        if (!string.IsNullOrEmpty(userId))
        {
            _userConnections[userId] = connectionId;
            await Groups.AddToGroupAsync(connectionId, userId);
            Console.WriteLine($"User {userId} added to group");
        }

        await base.OnConnectedAsync();
    }

    public async Task SetUserIdentifier(string userId)
    {
        var connectionId = Context.ConnectionId;
        Console.WriteLine($"SetUserIdentifier: UserId={userId}, ConnectionId={connectionId}");

        _userConnections[userId] = connectionId;
        await Groups.AddToGroupAsync(connectionId, userId);
        Console.WriteLine($"User {userId} added to group successfully");
    }

    // ✅ انضمام مستخدم إلى مجموعة
    public async Task JoinGroup(int groupId)
    {
        var connectionId = Context.ConnectionId;
        var groupName = $"group_{groupId}";
        await Groups.AddToGroupAsync(connectionId, groupName);
        Console.WriteLine($"Connection {connectionId} joined group {groupName}");
    }

    // ✅ مغادرة مستخدم من مجموعة
    public async Task LeaveGroup(int groupId)
    {
        var connectionId = Context.ConnectionId;
        var groupName = $"group_{groupId}";
        await Groups.RemoveFromGroupAsync(connectionId, groupName);
        Console.WriteLine($"Connection {connectionId} left group {groupName}");
    }

    // ✅ إرسال رسالة مجموعة
    public async Task SendGroupMessage(int groupId, int senderId, string message, string senderName)
    {
        var groupName = $"group_{groupId}";
        Console.WriteLine($"SendGroupMessage: Group={groupId}, Sender={senderId}, Msg={message}");

        try
        {
            await Clients.Group(groupName).SendAsync("ReceiveGroupMessage", groupId, senderId, message, DateTime.Now, senderName);
            Console.WriteLine($"Message sent to group {groupName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending group message: {ex.Message}");
        }
    }

    public async Task SendMessageToUser(int fromUserId, int toUserId, string message)
    {
        Console.WriteLine($"SendMessageToUser: From={fromUserId}, To={toUserId}, Msg={message}");

        try
        {
            await Clients.Group(toUserId.ToString())
                .SendAsync("ReceiveMessage", fromUserId, toUserId, message, DateTime.Now);
            Console.WriteLine($"Message sent to group {toUserId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message: {ex.Message}");
        }
    }

    public async Task MessageDelivered(int fromUserId, int toUserId)
    {
        Console.WriteLine($"MessageDelivered: From={fromUserId}, To={toUserId}");

        try
        {
            await Clients.Group(toUserId.ToString())
                .SendAsync("MessageDelivered", fromUserId, toUserId);
            Console.WriteLine($"Delivered notification sent to group {toUserId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending delivered notification: {ex.Message}");
        }
    }

    public async Task MessageRead(int fromUserId, int toUserId)
    {
        Console.WriteLine($"MessageRead: From={fromUserId}, To={toUserId}");
        await Clients.Group(toUserId.ToString())
            .SendAsync("MessageRead", fromUserId, toUserId);
    }

    public async Task SendTaskNotification(string notificationType, int taskId, int fromUserId, int toUserId, string taskDescription, DateTime timestamp)
    {
        Console.WriteLine($"Task notification: {notificationType} - Task {taskId} to user {toUserId}");
        await Clients.Group(toUserId.ToString())
            .SendAsync("ReceiveTaskNotification", notificationType, taskId, fromUserId, taskDescription, timestamp);
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var connectionId = Context.ConnectionId;
        Console.WriteLine($"Client disconnected: {connectionId}");

        var user = _userConnections.FirstOrDefault(x => x.Value == connectionId);
        if (!string.IsNullOrEmpty(user.Key))
        {
            _userConnections.Remove(user.Key);
        }

        await base.OnDisconnectedAsync(exception);
    }
}