using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CheckerServer
{
    public class Server
    {
        private static ConcurrentDictionary<WebSocket, bool> clients = new();
        private static Dictionary<Guid, Lobby> lobbies = new();

        private readonly HttpListener _listener;

        public Server()
        {
            _listener = new HttpListener();
        }

        public async Task StartAsync(string prefix)
        {
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            Program.Log($"Сервер запущен и слушает {prefix}");

            while (true)
            {
                try
                {
                    var context = await _listener.GetContextAsync();

                    if (context.Request.IsWebSocketRequest)
                    {
                        _ = Task.Run(() => HandleWebSocketAsync(context));
                    }
                    else
                    {
                        Program.LogError("Получен не WebSocket запрос.");
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (Exception ex)
                {
                    Program.LogError($"Ошибка сервера: {ex.Message}");
                }
            }
        }

        private async Task HandleWebSocketAsync(HttpListenerContext context)
        {
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                var webSocket = wsContext.WebSocket;

                clients[webSocket] = true;
                Program.Log("Клиент подключился по WebSocket.");

                var buffer = new byte[10240];

                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Program.Log("Клиент закрыл соединение.");
                        await BroadcastToLobby(webSocket, "Противник отключился.");
                        foreach (var lobby in lobbies.Values)
                        {
                            if (lobby.ContainsClient(webSocket))
                            {
                                var receiver = lobby.GetOtherClient(webSocket);
                                if (receiver != null && receiver.State == WebSocketState.Open)
                                {
                                    lobby.RemoveClient(webSocket);
                                }
                                break;
                            }
                        }
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Соединение закрыто клиентом", CancellationToken.None);
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Program.Log($"Получено сообщение: {message}");

                    string response = await HandleResponse(message, webSocket);
                    if (response == "close")
                    {
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "попросил", CancellationToken.None);
                        return;
                    }
                    byte[] responseBuffer = Encoding.UTF8.GetBytes(response);
                    await webSocket.SendAsync(new ArraySegment<byte>(responseBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Program.LogError($"Ошибка обработки WebSocket: {ex.Message}");
            }
        }

        private async Task<string> HandleResponse(string message, WebSocket socket)
        {
            if (message.StartsWith("join:"))
            {
                var guidString = message.Split(':')[1];
                if (Guid.TryParse(guidString, out Guid lobbyId) && lobbies.TryGetValue(lobbyId, out Lobby lobby))
                {
                    if (lobby.AddClient(socket))
                    {
                        Console.WriteLine($"Клиент присоединился к лобби {lobbyId}");
                        var receiver = lobby.GetOtherClient(socket);
                        if (receiver != null && receiver.State == WebSocketState.Open)
                        {
                            try
                            {
                                var messageBytes = Encoding.UTF8.GetBytes("start");
                                await receiver.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                                await socket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                                Program.Log($"Игра началась");
                            }
                            catch (WebSocketException)
                            {
                                Program.LogError("Ошибка при отправке сообщения другому клиенту в лобби");
                            }
                        }
                        clients.TryRemove(socket, out _);
                        //return ("Вы успешно присоединились к лобби");
                    }
                    else
                    {
                        //return ("Лобби заполнено");
                    }
                }
                else
                {
                    //return ("Лобби не найдено");
                }
                await LobbyNotify();
            }
            else if (message == "get_lobbies")
            {
                var lobbyList = lobbies
                     .Where(lobby => lobby.Value.Connected < 2)
                     .Select(lobby => new
                     {
                         LobbyName = lobby.Value.Name,
                         Connected = lobby.Value.Connected,
                         LobbyId = lobby.Key
                     })
                     .ToList();

                string json = JsonSerializer.Serialize(lobbyList);
                return (json);
            }
            else if (message.StartsWith("create_lobby:"))
            {
                var lobbyName = message.Split(':')[1];
                Lobby lobby = new Lobby(lobbyName);
                //lobby.AddClient(socket);
                lobby.ClientDisconnected += Lobby_ClientDisconnected;
                lobbies.Add(lobby.Id, lobby);
                await LobbyNotify();
                return ($"created:{lobby.Id.ToString()}");
            }
            else
            {
                Program.Log("Отправляю другим участникам лобби");
                await BroadcastToLobby(socket, message);
                Program.Log("Отправил другим участникам лобби");
            }
            return "";
        }

        private async void Lobby_ClientDisconnected(WebSocket sender)
        {
            await BroadcastToLobby(sender, "Противник отключился.");
        }

        static async Task BroadcastToLobby(WebSocket sender, string message)
        {
            Program.LogError($"Кол-во Лобби: {lobbies.Count}");
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
                            Program.Log($"Сообщение отправлено другому клиенту в лобби");
                            if (message.StartsWith("action"))
                            {
                                Action action = JsonSerializer.Deserialize<Action>(message.Substring(7).Trim());
                                if (!action.iscapture)
                                {
                                    lobby.NotifySwapSides();
                                    Program.Log("Ходы поменялись.");
                                }
                                else if (!action.HasCaptureMoves)
                                {
                                    lobby.NotifySwapSides();
                                    Program.Log("Ходы поменялись.");
                                }
                            }
                            else if (message == "Противник отключился.")
                            {
                                lobby.RemoveClient(sender);
                                lobby.RemoveClient(receiver);
                            }
                        }
                        catch (WebSocketException)
                        {
                            Program.LogError("Ошибка при отправке сообщения другому клиенту в лобби");
                        }
                    }
                    break;
                }
            }
        }
        private async Task LobbyNotify()
        {
            var lobbyList = lobbies
                     .Where(lobby => lobby.Value.Connected < 2)
                     .Select(lobby => new
                     {
                         LobbyName = lobby.Value.Name,
                         Connected = lobby.Value.Connected,
                         LobbyId = lobby.Key
                     })
                     .ToList();
            string json = JsonSerializer.Serialize(lobbyList);
            string message = json;
            foreach (var client in clients.Keys)
            {
                try
                {
                    var messageBytes = Encoding.UTF8.GetBytes(message);
                    await client.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    Program.Log($"Клиенту сказали обновится☺");
                }
                catch (WebSocketException)
                {
                    Program.LogError("Ошибка при отправке обновления клиенту");
                }
            }
        }
    }
}
