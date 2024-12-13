using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace CheckerServer
{
    class Lobby
    {
        private WebSocket client1;
        private WebSocket client2;
        private CancellationTokenSource _timerCts;

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

        public event Action<WebSocket> ClientDisconnected;

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
                ClientDisconnected?.Invoke(client);
                return true;
            }
            else if (client == client2)
            {
                client2 = null;
                ClientDisconnected?.Invoke(client);
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

        private void StartTimer()
        {
            _timerCts?.Cancel();
            _timerCts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    while (!_timerCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(60000, _timerCts.Token);
                        if (!_timerCts.Token.IsCancellationRequested)
                        {
                            await NotifySwapSides();
                        }
                    }
                }
                catch (TaskCanceledException)
                {

                }
            }, _timerCts.Token);
        }

        public async Task NotifySwapSides()
        {
            string message = "swap";
            if (client1 != null && client1.State == WebSocketState.Open)
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);
                await client1.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            if (client2 != null && client2.State == WebSocketState.Open)
            {
                var messageBytes = Encoding.UTF8.GetBytes(message);
                await client2.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            ResetTimer();
        }

        public void ResetTimer()
        {
            StartTimer();
        }
    }
}
