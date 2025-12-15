using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace MBDManager.Services
{
    public class AgvService
    {
        private readonly HttpClient _client;

        // ★ [수정] 설정 파일에서 IP 가져오기
        public string AgvIpAddress => SettingsService.Instance.Settings.AgvIpAddress;

        public AgvService()
        {
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(1.5); // 타임아웃 1.5초
        }

        // 명령 전송 (taskA, stop 등)
        public async Task<bool> SendCommandAsync(string command)
        {
            if (string.IsNullOrEmpty(AgvIpAddress)) return false;

            try
            {
                string url = $"http://{AgvIpAddress}/{command}";
                var response = await _client.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AGV 명령 오류: {ex.Message}");
                return false;
            }
        }

        // 연결 확인 (Ping)
        public async Task<bool> CheckConnectionAsync()
        {
            if (string.IsNullOrEmpty(AgvIpAddress)) return false;

            try
            {
                string url = $"http://{AgvIpAddress}/";
                var response = await _client.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}