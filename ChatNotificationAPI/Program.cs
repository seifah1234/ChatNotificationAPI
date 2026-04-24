using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
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
    // FIX: Use thread-safe dictionary
    private static readonly ConcurrentDictionary<string, string> _userConnections = new();
    private static readonly ConcurrentDictionary<string, HashSet<string>> _userConnectionSets = new();

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        Console.WriteLine($"Client connected: {connectionId}");

        var httpContext = Context.GetHttpContext();
        var userId = httpContext?.Request.Query["userId"].ToString();

        if (!string.IsNullOrEmpty(userId))
        {
            // Store connection
            _userConnections[userId] = connectionId;

            // Track multiple connections for same user
            _userConnectionSets.AddOrUpdate(userId,
                _ => new HashSet<string> { connectionId },
                (_, set) => { set.Add(connectionId); return set; });

            await Groups.AddToGroupAsync(connectionId, userId);
            Console.WriteLine($"User {userId} connected and added to group");
        }

        await base.OnConnectedAsync();
    }

    public async Task SetUserIdentifier(string userId)
    {
        var connectionId = Context.ConnectionId;
        Console.WriteLine($"SetUserIdentifier: UserId={userId}, ConnectionId={connectionId}");

        _userConnections[userId] = connectionId;

        _userConnectionSets.AddOrUpdate(userId,
            _ => new HashSet<string> { connectionId },
            (_, set) => { set.Add(connectionId); return set; });

        await Groups.AddToGroupAsync(connectionId, userId);
        Console.WriteLine($"User {userId} registered successfully");
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

    // FIX: Add GroupMessageEdited handler
    public async Task GroupMessageEdited(int messageId, int groupId, string newText)
    {
        var groupName = $"group_{groupId}";
        Console.WriteLine($"GroupMessageEdited: MessageId={messageId}, GroupId={groupId}");

        try
        {
            await Clients.Group(groupName).SendAsync("GroupMessageEdited", messageId, groupId, newText);
            Console.WriteLine($"Edit notification sent to group {groupName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending edit notification: {ex.Message}");
        }
    }

    // FIX: Add GroupMessageDeleted handler
    public async Task GroupMessageDeleted(int messageId, int groupId)
    {
        var groupName = $"group_{groupId}";
        Console.WriteLine($"GroupMessageDeleted: MessageId={messageId}, GroupId={groupId}");

        try
        {
            await Clients.Group(groupName).SendAsync("GroupMessageDeleted", messageId, groupId);
            Console.WriteLine($"Delete notification sent to group {groupName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending delete notification: {ex.Message}");
        }
    }

    public async Task MarkGroupMessagesRead(int groupId, int userId)
    {
        var groupName = $"group_{groupId}";
        await Clients.Group(groupName)
            .SendAsync("GroupMessagesRead", groupId, userId, DateTime.Now);
    }

    public async Task SendMessageToUser(int fromUserId, int toUserId, string message)
    {
        Console.WriteLine($"SendMessageToUser: From={fromUserId}, To={toUserId}, Msg={message}");

        try
        {
            // FIX: Send to BOTH users' groups so sender gets confirmation too
            await Clients.Group(toUserId.ToString())
                .SendAsync("ReceiveMessage", fromUserId, toUserId, message, DateTime.Now);

            // Also send to sender for confirmation
            await Clients.Group(fromUserId.ToString())
                .SendAsync("ReceiveMessage", fromUserId, toUserId, message, DateTime.Now);

            Console.WriteLine($"Message sent to groups {toUserId} and {fromUserId}");
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
            // FIX: Send to both users
            await Clients.Group(toUserId.ToString())
                .SendAsync("MessageDelivered", fromUserId, toUserId);
            await Clients.Group(fromUserId.ToString())
                .SendAsync("MessageDelivered", fromUserId, toUserId);
            Console.WriteLine($"Delivered notification sent");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending delivered notification: {ex.Message}");
        }
    }

    public async Task MessageRead(int fromUserId, int toUserId)
    {
        Console.WriteLine($"MessageRead: From={fromUserId}, To={toUserId}");

        try
        {
            // FIX: Send to both users
            await Clients.Group(toUserId.ToString())
                .SendAsync("MessageRead", fromUserId, toUserId);
            await Clients.Group(fromUserId.ToString())
                .SendAsync("MessageRead", fromUserId, toUserId);
            Console.WriteLine($"Read notification sent");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending read notification: {ex.Message}");
        }
    }

    // FIX: Make sure this sends to both users
    public async Task MessageDeleted(int messageId, int toUserId)
    {
        Console.WriteLine($"MessageDeleted: MessageId={messageId}, To={toUserId}");

        try
        {
            await Clients.Group(toUserId.ToString())
                .SendAsync("MessageDeleted", messageId);

            // FIX: Get sender ID from context and notify them too
            var connectionId = Context.ConnectionId;
            var senderEntry = _userConnections.FirstOrDefault(x => x.Value == connectionId);
            if (!string.IsNullOrEmpty(senderEntry.Key))
            {
                await Clients.Group(senderEntry.Key)
                    .SendAsync("MessageDeleted", messageId);
            }

            Console.WriteLine($"Delete notification sent");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending delete notification: {ex.Message}");
        }
    }

    // FIX: Make sure this sends to both users
    public async Task MessageEdited(int messageId, int toUserId, string newText)
    {
        Console.WriteLine($"MessageEdited: MessageId={messageId}, To={toUserId}");

        try
        {
            await Clients.Group(toUserId.ToString())
                .SendAsync("MessageEdited", messageId, newText);

            // FIX: Get sender ID from context and notify them too
            var connectionId = Context.ConnectionId;
            var senderEntry = _userConnections.FirstOrDefault(x => x.Value == connectionId);
            if (!string.IsNullOrEmpty(senderEntry.Key))
            {
                await Clients.Group(senderEntry.Key)
                    .SendAsync("MessageEdited", messageId, newText);
            }

            Console.WriteLine($"Edit notification sent");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending edit notification: {ex.Message}");
        }
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

        // FIX: Clean up all connection tracking
        var userEntry = _userConnections.FirstOrDefault(x => x.Value == connectionId);
        if (!string.IsNullOrEmpty(userEntry.Key))
        {
            // Remove from main dictionary
            _userConnections.TryRemove(userEntry.Key, out _);

            // Remove from connection set
            if (_userConnectionSets.TryGetValue(userEntry.Key, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                {
                    _userConnectionSets.TryRemove(userEntry.Key, out _);
                }
            }

            Console.WriteLine($"User {userEntry.Key} disconnected");
        }

        await base.OnDisconnectedAsync(exception);
    }
}