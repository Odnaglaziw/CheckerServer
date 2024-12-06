using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class MultiplayerServer
{
    // Потокобезопасный словарь для хранения активных подключений
    private static ConcurrentDictionary<WebSocket, bool> clients = new();

    // Список лобби
    private static Dictionary<Guid, Lobby> lobbies = new();

    static async Task Main(string[] args)
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://+:65000/");
        listener.Start();
        Console.WriteLine("WebSocket сервер запущен на порту 65000");

        while (true)
        {
            var context = await listener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                var webSocketContext = await context.AcceptWebSocketAsync(null);
                Console.WriteLine("Новое подключение");
                var socket = webSocketContext.WebSocket;
                clients[socket] = true; // Добавляем нового клиента
                _ = HandleConnection(socket);
            }
            else
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
            }
        }
    }

    static async Task HandleConnection(WebSocket socket)
    {
        byte[] buffer = new byte[1024];
        while (socket.State == WebSocketState.Open)
        {
            try
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine($"Получено сообщение: {message}");

                if (message.StartsWith("join:"))
                {
                    var guidString = message.Split(':')[1];
                    if (Guid.TryParse(guidString, out Guid lobbyId) && lobbies.TryGetValue(lobbyId, out Lobby lobby))
                    {
                        if (lobby.AddClient(socket))
                        {
                            Console.WriteLine($"Клиент присоединился к лобби {lobbyId}");
                            await SendMessage(socket, "Вы успешно присоединились к лобби");
                        }
                        else
                        {
                            await SendMessage(socket, "Лобби заполнено");
                        }
                    }
                    else
                    {
                        await SendMessage(socket, "Лобби не найдено");
                    }
                }
                else if (message == "get_lobbies")
                {
                    var lobbyList = lobbies.Select(lobby => new
                    {
                        LobbyName = lobby.Value.Name,
                        Connected = lobby.Value.Connected,
                        LobbyId = lobby.Key
                    }).ToList();

                    string json = JsonSerializer.Serialize(lobbyList);
                    await SendMessage(socket, json);

                    foreach (var lobby in lobbies)
                    {
                        Console.WriteLine($"{lobby.Value.Name}: {lobby.Key}");
                    }
                }
                else if (message.StartsWith("create_lobby:"))
                {
                    var lobbyName = message.Split(':')[1];
                    Lobby lobby = new Lobby(lobbyName);
                    lobby.AddClient(socket);
                    lobbies.Add(lobby.Id, lobby);
                    await SendMessage(socket,$"created:{lobby.Id.ToString()}");
                    foreach(var xe in lobbies)
                    {
                        Console.WriteLine($"{xe.Value.Name}: {xe.Key}");
                    }
                }
                else
                {
                    // Рассылка сообщения второму клиенту в лобби
                    await BroadcastToLobby(socket, message);
                }
            }
            catch (WebSocketException)
            {
                Console.WriteLine("Клиент отключился");
                break;
            }
        }

        // Удаляем клиента из списка, если соединение закрыто
        clients.TryRemove(socket, out _);
        RemoveClientFromLobbies(socket);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Соединение закрыто", CancellationToken.None);
    }

    static async Task BroadcastToLobby(WebSocket sender, string message)
    {
        foreach (var lobby in lobbies.Values)
        {
            if (lobby.ContainsClient(sender))
            {
                var receiver = lobby.GetOtherClient(sender);
                if (receiver != null && receiver.State == WebSocketState.Open)
                {
                    try
                    {
                        var messageBytes = Encoding.UTF8.GetBytes(message);
                        await receiver.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                        Console.WriteLine($"Сообщение отправлено другому клиенту в лобби");
                    }
                    catch (WebSocketException)
                    {
                        Console.WriteLine("Ошибка при отправке сообщения");
                    }
                }
                break;
            }
        }
    }

    static void RemoveClientFromLobbies(WebSocket client)
    {
        foreach (var lobby in lobbies.Values)
        {
            if (lobby.RemoveClient(client))
            {
                Console.WriteLine("Клиент удалён из лобби");
                break;
            }
        }
    }

    static async Task SendMessage(WebSocket socket, string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        Console.WriteLine($"Сообщение отправленно: {message}");
    }

    class Lobby
    {
        private WebSocket client1;
        private WebSocket client2;
        public string Name { get; private set; }
        public int Connected
        {
            get
            {
                int sum = 0;
                if (client1 != null) sum++;
                if (client2 != null) sum++;
                return sum;
            }
        }
        public Guid Id { get; } = Guid.NewGuid();
        public Lobby(string name)
        {
            Name = name;
        }

        public bool AddClient(WebSocket client)
        {
            if (client1 == null)
            {
                client1 = client;
                return true;
            }
            else if (client2 == null)
            {
                client2 = client;
                return true;
            }
            return false;
        }

        public bool RemoveClient(WebSocket client)
        {
            if (client == client1)
            {
                client1 = null;
                return true;
            }
            else if (client == client2)
            {
                client2 = null;
                return true;
            }
            return false;
        }

        public bool ContainsClient(WebSocket client)
        {
            return client == client1 || client == client2;
        }

        public WebSocket GetOtherClient(WebSocket client)
        {
            if (client == client1) return client2;
            if (client == client2) return client1;
            return null;
        }
    }
}
