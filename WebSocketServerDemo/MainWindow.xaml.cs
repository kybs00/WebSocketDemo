using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WebSocketServerDemo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                ServicePointManager.DefaultConnectionLimit = Environment.ProcessorCount * 10;
                ServicePointManager.UseNagleAlgorithm = false;
                _listener = new HttpListener
                {
                    IgnoreWriteExceptions = true,
                    AuthenticationSchemes = AuthenticationSchemes.Anonymous
                };
                _listener.Prefixes.Add($"http://+:{25410}/");
                Start();
            });
        }
        public void Start()
        {
            if (_listener.IsListening)
            {
                return;
            }
            _listener.Start();
            Task.Factory.StartNew(async () =>
            {
                //循环监听客户端连接
                while (true)
                {
                    //有客户端连接时，产生一个HttpListenerContext上下文
                    var context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        //与客户端建立连接
                        var webSocketContext = await context.AcceptWebSocketAsync(null);
                        var webSocket = webSocketContext.WebSocket;
                        var serverIp = webSocketContext.RequestUri.Host;
                        var channel = webSocketContext.RequestUri.LocalPath.TrimStart('/');
                        var deviceName = context.Request.Cookies["ClientName"]?.Value;
                        if (!string.IsNullOrWhiteSpace(deviceName))
                        {
                            deviceName = Uri.UnescapeDataString(deviceName);
                        }
                        Console.WriteLine($"Client connected:{deviceName}->{serverIp}/{channel}");
                        if (string.IsNullOrEmpty(deviceName))
                        {
                            throw new InvalidOperationException($"请求连接客户端的名称是空的，{webSocketContext.RequestUri}");
                        }

                        AddClientAsync(serverIp, channel,deviceName, webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.StatusDescription = "not websocket request";
                        context.Response.Close();
                    }
                }
            }, TaskCreationOptions.LongRunning);
        }
        private async void AddClientAsync(string serverIp, string channel,  string deviceName, WebSocket webSocket)
        {
            //添加客户端
            var clientId = Guid.NewGuid().ToString();
            _clientDict.TryAdd(clientId, webSocket);
            try
            {
                // 处理接收消息的循环
                byte[] buffer = new byte[BufferSize];
                ArraySegment<byte> receiveSegment = new ArraySegment<byte>(buffer);
                // 循环接收消息
                while (webSocket.State == WebSocketState.Open)
                {
                    var receiveResult = await webSocket.ReceiveAsync(receiveSegment, CancellationToken.None);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        //客户端关闭
                        break;
                    }
                    if (receiveResult.MessageType == WebSocketMessageType.Text)
                    {
                        string data = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                        //HandleReceivedMessageAsync(serverIp, channel, deviceIp, deviceName, webSocket, data);
                    }
                    else if (receiveResult.MessageType == WebSocketMessageType.Binary)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "服务端暂不支持字节数据", CancellationToken.None);
                    }
                }
            }
            catch (Exception e)
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    //主动关闭客户端
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, $"接收端异常,{e.Message},{e.StackTrace}", CancellationToken.None);
                }
            }
            finally
            {
                // 客户端断开连接时，从字典中移除
                _clientDict.TryRemove(clientId, out _);
                if (webSocket.State == WebSocketState.CloseReceived || webSocket.State == WebSocketState.Open)
                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, $"正常关闭", CancellationToken.None);
                webSocket.Dispose();
            }
        }
        public async Task BroadcastAsync<TInput>(string eventName, TInput data)
        {
            var channelClients = _clientDict.Select(i => i.Value).ToList();
            foreach (var channelClient in channelClients)
            {
                if (channelClient.State == WebSocketState.Connecting)
                {
                    continue;
                }
                var message = data as string ?? JsonSerializer.Serialize(data);
                var bytes = Encoding.UTF8.GetBytes(message);
                await channelClient.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        /// <summary>
        /// eventId、ClientIP
        /// </summary>
        private readonly ConcurrentDictionary<string, WebSocket> _clientDict = new ConcurrentDictionary<string, WebSocket>();
        private HttpListener _listener;
        private const int BufferSize = 1024 * 1024;
        private SemaphoreSlim _sendLock = new SemaphoreSlim(1);
        private void BroadcastButton_OnClick(object sender, RoutedEventArgs e)
        {
            var timer = new System.Timers.Timer();
            timer.Interval = 1;
            timer.Elapsed += Timer_Elapsed;
            timer.Start();
        }
        private async void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var message = $"{DateTime.Now.ToString("HH:mm:ss fff")},hello from server";

            //await _sendLock.WaitAsync();
            await BroadcastAsync("test", message);
            //_sendLock.Release();

            Console.WriteLine(message);
        }
    }
}