using System.Diagnostics;
using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WebSocketDemo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ClientWebSocket _webSocket;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void ConnectButton_OnClick(object sender, RoutedEventArgs e)
        {
            await ConnectSocketAsync();
        }
        private async Task ConnectSocketAsync()
        {
            _webSocket = new ClientWebSocket();
            _webSocket.Options.Cookies = new CookieContainer();
            var serverIp = "localhost";
            var host = 25410;
            var channel = "Default";
            var uri = new Uri($"ws://{serverIp}:{host}/{channel}");
            //请求头不能包含非ASCII字符
            _webSocket.Options.Cookies.Add(uri, new Cookie("ClientName", Environment.MachineName));
            await _webSocket.ConnectAsync(uri, CancellationToken.None);
            //连接成功后，创建Socket消息监听
            await Task.Factory.StartNew(CreateSocketListening, TaskCreationOptions.LongRunning);
        }
        private async Task CreateSocketListening()
        {
            try
            {
                byte[] buffer = new byte[ReceiveBuffer];
                ArraySegment<byte> receiveSegment = new ArraySegment<byte>(buffer);
                while (_webSocket?.State == WebSocketState.Open)
                {
                    //关闭之后， ReceiveAsync 则抛出关闭远程主机连接
                    var receiveResult = await _webSocket.ReceiveAsync(receiveSegment, CancellationToken.None);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    if (receiveResult.MessageType == WebSocketMessageType.Text)
                    {
                        string receivedData = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                        Console.WriteLine($"{DateTime.Now.ToString("HH:mm:ss fff")} Received:{receivedData} ");
                    }
                    else if (receiveResult.MessageType == WebSocketMessageType.Binary)
                    {
                        //ReceiveBinary(receiveResult, buffer);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"SocketClient接收异常，{e.Message}");
                //关闭客户端
                if (_webSocket?.State == WebSocketState.Open)
                    await _webSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, $"接收端异常,{e}", CancellationToken.None)!;
            }
            finally
            {
                _webSocket?.Dispose();
                _webSocket = null;
            }
        }
        /// <summary>接收缓冲区大小</summary>
        public int ReceiveBuffer = 1024 * 1024;
        //数据包的长度
        private int _binaryPackLength;

    }
}