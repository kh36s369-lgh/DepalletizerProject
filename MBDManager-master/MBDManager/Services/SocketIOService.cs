using SocketIOClient; // 이 using은 유지
using System;
using System.Threading.Tasks;

namespace MBDManager.Services
{
    public class SocketIOService : IDisposable
    {
        // [수정 1] 타입을 명확하게 SocketIOClient.SocketIO 로 지정
        private readonly SocketIOClient.SocketIO _client;

        public event Action OnConnected;
        public event Action OnDisconnected;

        public SocketIOService(string url)
        {
            // [수정] 옵션 추가 (EIO = 4 설정 필수)
            var options = new SocketIOClient.SocketIOOptions
            {
                Transport = SocketIOClient.Transport.TransportProtocol.WebSocket // WebSocket 강제 사용
            };

            _client = new SocketIOClient.SocketIO(url, options);

            // ... (이벤트 핸들러 등록 코드는 그대로 유지) ...
            _client.OnConnected += async (sender, e) =>
            {
                OnConnected?.Invoke();
                await Task.CompletedTask;
            };

            _client.OnDisconnected += (sender, e) =>
            {
                OnDisconnected?.Invoke();
            };
        }

        public async Task ConnectAsync()
        {
            try
            {
                await _client.ConnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"연결 실패: {ex.Message}");
            }
        }

        public async Task DisconnectAsync()
        {
            if (_client.Connected)
            {
                await _client.DisconnectAsync();
            }
        }

        // 데이터 전송 (Emit)
        public async Task EmitAsync(string eventName, object data)
        {
            if (_client.Connected)
            {
                await _client.EmitAsync(eventName, data);
            }
        }

        // 데이터 수신 (On)
        public void On<T>(string eventName, Action<T> callback)
        {
            _client.On(eventName, response =>
            {
                // Socket.IO 응답 처리
                // DogHappy.SocketIOClient는 response.GetValue<T>()를 사용합니다.
                try
                {
                    var data = response.GetValue<T>();
                    callback(data);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"데이터 파싱 오류 ({eventName}): {ex.Message}");
                }
            });
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}