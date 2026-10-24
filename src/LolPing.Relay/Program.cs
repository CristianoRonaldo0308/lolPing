// lolPing relay: chuyển tiếp gói tin đã mã hoá giữa những người cùng phòng.
// Server không có khoá nên không đọc được nội dung; nó chỉ biết RoomId (mã băm của mã phòng).
//
// Chạy thử trên máy:  dotnet run --project src/LolPing.Relay   →  ws://localhost:8080
// Deploy miễn phí:    xem README.md (Render / Fly.io / Docker)

using System.Collections.Concurrent;
using System.Net.WebSockets;

const int MaxMembers = 8;
const int MaxMessageBytes = 8 * 1024 * 1024;
const WebSocketCloseStatus RoomFull = (WebSocketCloseStatus)4001;

var builder = WebApplication.CreateSlimBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

var rooms = new ConcurrentDictionary<string, Room>();

app.MapGet("/", () => Results.Text($"lolPing relay OK · {rooms.Count} room(s) · {rooms.Values.Sum(r => r.Count)} client(s)"));

app.Map("/ws", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    var roomId = ctx.Request.Query["room"].ToString();
    if (roomId.Length != 32 || !roomId.All(Uri.IsHexDigit))
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var client = new Client(ws);

    Room room;
    while (true)
    {
        room = rooms.GetOrAdd(roomId, _ => new Room());
        var added = room.TryAdd(client, MaxMembers);
        if (added == AddResult.Closed)
        {
            rooms.TryRemove(new KeyValuePair<string, Room>(roomId, room)); // phòng cũ vừa trống: tạo phòng mới
            continue;
        }
        if (added == AddResult.Full)
        {
            await ws.CloseAsync(RoomFull, "room full", CancellationToken.None);
            return;
        }
        break;
    }

    try
    {
        await Pump(client, room, ctx.RequestAborted);
    }
    catch (Exception) { /* client rớt mạng */ }
    finally
    {
        if (room.Remove(client)) rooms.TryRemove(new KeyValuePair<string, Room>(roomId, room));
        if (ws.State == WebSocketState.Open)
        {
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch { }
        }
    }
});

app.Run();

async Task Pump(Client client, Room room, CancellationToken ct)
{
    var buffer = new byte[64 * 1024];
    using var ms = new MemoryStream();
    while (client.Socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
    {
        var r = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
        if (r.MessageType == WebSocketMessageType.Close) break;
        ms.Write(buffer, 0, r.Count);
        if (ms.Length > MaxMessageBytes)
        {
            await client.Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "too big", CancellationToken.None);
            break;
        }
        if (!r.EndOfMessage) continue;

        if (r.MessageType == WebSocketMessageType.Binary)
        {
            var data = ms.ToArray();
            foreach (var other in room.Others(client))
                _ = other.SendAsync(data);
        }
        ms.SetLength(0);
    }
}

enum AddResult { Added, Full, Closed }

sealed class Room
{
    private readonly List<Client> _clients = new();
    private bool _closed;

    public int Count { get { lock (_clients) return _clients.Count; } }

    public AddResult TryAdd(Client c, int max)
    {
        lock (_clients)
        {
            if (_closed) return AddResult.Closed;
            if (_clients.Count >= max) return AddResult.Full;
            _clients.Add(c);
            return AddResult.Added;
        }
    }

    /// <returns>true nếu phòng trống và đã đóng (cần xoá khỏi danh sách).</returns>
    public bool Remove(Client c)
    {
        lock (_clients)
        {
            _clients.Remove(c);
            if (_clients.Count > 0) return false;
            _closed = true;
            return true;
        }
    }

    public Client[] Others(Client me)
    {
        lock (_clients) return _clients.Where(x => x != me).ToArray();
    }
}

sealed class Client(WebSocket socket)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    public WebSocket Socket { get; } = socket;

    public async Task SendAsync(byte[] data)
    {
        try
        {
            await _lock.WaitAsync();
            try
            {
                if (Socket.State == WebSocketState.Open)
                    await Socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            finally
            {
                _lock.Release();
            }
        }
        catch { /* client chậm/rớt mạng: bỏ qua gói này */ }
    }
}
