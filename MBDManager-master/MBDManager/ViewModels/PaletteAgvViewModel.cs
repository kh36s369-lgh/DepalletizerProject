using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MBDManager.Messages;
using MBDManager.Services;
using System;
using System.Collections.Generic; // List용
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows; // Application.Current용

namespace MBDManager.ViewModels
{
    public partial class PaletteAgvViewModel : ObservableObject
    {
        private readonly AgvService _agvService = new AgvService();

        // 소켓 서비스 참조를 위해 가져옵니다. (싱글톤 방식 활용 or 직접 생성)
        private readonly SocketIOService _socketService;

        [ObservableProperty]
        private ObservableCollection<PaletteZoneModel> _paletteStatuses = new();

        [ObservableProperty]
        private ObservableCollection<AgvHistoryModel> _agvHistory = new();

        [ObservableProperty] private string _agvConnectionStatus = "대기 중";
        [ObservableProperty] private string _agvCurrentAction = "IDLE";
        [ObservableProperty] private string _agvConnectionStatusEnum = "Normal";

        public PaletteAgvViewModel()
        {
            // Socket 서비스 연결 (MainWindowViewModel과 같은 주소)
            _socketService = new SocketIOService("http://127.0.0.1:5000");

            // ★ 서버에서 기록 목록이 오면 처리하는 리스너 등록
            _socketService.On<List<dynamic>>("agv_history_response", OnHistoryReceived);
            _ = _socketService.ConnectAsync();

            // 구역 초기화
            PaletteStatuses.Add(new PaletteZoneModel("A", 0, 4));
            PaletteStatuses.Add(new PaletteZoneModel("B", 0, 4));
            PaletteStatuses.Add(new PaletteZoneModel("C", 0, 4));
            PaletteStatuses.Add(new PaletteZoneModel("D", 0, 4));

            // 초기 연결 확인
            _ = InitializeAgvConnection();

            // ★ DB에 저장된 과거 기록 불러오기 요청
            RequestHistoryLoad();

            // 메시지 수신 (자동 호출 로직)
            WeakReferenceMessenger.Default.Register<PaletteUpdateMessage>(this, async (r, m) =>
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    var target = PaletteStatuses.FirstOrDefault(p => p.ZoneName == m.Value.Zone);
                    if (target != null)
                    {
                        target.CurrentCount = m.Value.Count;

                        if (target.IsFull)
                        {
                            string actionMsg = "자동 회수 요청 (Full)";

                            // 1. 화면에 추가
                            AddHistoryToUi(target.ZoneName, actionMsg);
                            // 2. ★ DB로 전송
                            await SaveHistoryToDb(target.ZoneName, actionMsg);

                            AgvConnectionStatus = "AGV 호출 중...";
                            AgvConnectionStatusEnum = "Warning";

                            string command = $"task{target.ZoneName}";
                            bool success = await _agvService.SendCommandAsync(command);

                            if (success)
                            {
                                AgvCurrentAction = $"{target.ZoneName}구역으로 이동 중 🚚";
                                AgvConnectionStatus = "정상 주행 중";
                                AgvConnectionStatusEnum = "Normal";
                                MainWindowViewModel.GlobalLogAction?.Invoke($"AGV {target.ZoneName}구역 자동 호출 성공", "Info", "AGV");
                            }
                            else
                            {
                                AgvCurrentAction = "통신 실패 ❌";
                                AgvConnectionStatus = "연결 끊김";
                                AgvConnectionStatusEnum = "Error";
                                MainWindowViewModel.GlobalLogAction?.Invoke($"AGV {target.ZoneName}구역 자동 호출 실패", "Error", "AGV");
                            }
                        }
                    }
                });
            });
        }

        // ★ [신규] DB에서 가져온 기록을 UI에 업데이트
        private void OnHistoryReceived(List<dynamic> data)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AgvHistory.Clear();
                foreach (var item in data)
                {
                    // JSON 파싱 (dynamic 타입 처리)
                    string tsStr = item.GetProperty("timestamp").ToString();
                    string zone = item.GetProperty("zone").ToString();
                    string action = item.GetProperty("action").ToString();

                    DateTime.TryParse(tsStr, out DateTime dt);

                    AgvHistory.Add(new AgvHistoryModel
                    {
                        Timestamp = dt,
                        Zone = zone,
                        Action = action
                    });
                }
            });
        }

        // ★ [신규] 서버에 기록 목록 요청
        private async void RequestHistoryLoad()
        {
            // 소켓 연결될 때까지 잠시 대기 후 요청
            await Task.Delay(1000);
            await _socketService.EmitAsync("get_agv_history", new { });
        }

        // ★ [신규] 기록 저장 함수 (중복 코드 제거용)
        private async Task SaveHistoryToDb(string zone, string action)
        {
            await _socketService.EmitAsync("save_agv_action", new { zone = zone, action = action });
        }

        // ★ [신규] UI 추가 함수 (중복 코드 제거용)
        private void AddHistoryToUi(string zone, string action)
        {
            AgvHistory.Insert(0, new AgvHistoryModel
            {
                Timestamp = DateTime.Now,
                Zone = zone,
                Action = action
            });
        }

        private async Task InitializeAgvConnection()
        {
            MainWindowViewModel.GlobalLogAction?.Invoke("AGV 연결 상태 확인 중...", "Info", "System");
            bool isConnected = await _agvService.CheckConnectionAsync();

            if (isConnected)
            {
                AgvConnectionStatus = "온라인 (대기 중)";
                AgvConnectionStatusEnum = "Normal";
                AgvCurrentAction = "IDLE (명령 대기)";
                MainWindowViewModel.GlobalLogAction?.Invoke("AGV(Arduino) 연결 성공", "Info", "AGV");
            }
            else
            {
                AgvConnectionStatus = "오프라인";
                AgvConnectionStatusEnum = "Error";
                AgvCurrentAction = "장치 없음";
                MainWindowViewModel.GlobalLogAction?.Invoke("AGV 연결 실패 (응답 없음)", "Error", "AGV");
            }
        }

        // [수동 버튼]
        [RelayCommand]
        private async Task RequestAgv(string zoneName)
        {
            string command = $"task{zoneName}";
            bool success = await _agvService.SendCommandAsync(command);
            string actionMsg = "수동 호출 명령";

            // 1. 화면 추가 & 2. DB 저장
            AddHistoryToUi(zoneName, actionMsg);
            await SaveHistoryToDb(zoneName, actionMsg);

            if (success)
            {
                AgvCurrentAction = $"{zoneName}구역으로 이동 중 (수동)";
                AgvConnectionStatus = "정상 주행 중";
                AgvConnectionStatusEnum = "Normal";
                MainWindowViewModel.GlobalLogAction?.Invoke($"AGV {zoneName}구역 수동 호출 성공", "Info", "AGV");
            }
            else
            {
                AgvCurrentAction = "통신 실패 ❌";
                AgvConnectionStatusEnum = "Error";
                MainWindowViewModel.GlobalLogAction?.Invoke($"AGV {zoneName}구역 수동 호출 실패", "Error", "AGV");
            }
        }

        // [정지 버튼]
        [RelayCommand]
        private async Task StopAgv()
        {
            bool success = await _agvService.SendCommandAsync("stop");
            AgvCurrentAction = "강제 정지됨";

            // 정지도 기록에 남기고 싶으면 아래 주석 해제
            // AddHistoryToUi("ALL", "비상 정지");
            // await SaveHistoryToDb("ALL", "비상 정지");

            if (success) MainWindowViewModel.GlobalLogAction?.Invoke("AGV 비상 정지 성공", "Warning", "AGV");
            else MainWindowViewModel.GlobalLogAction?.Invoke("AGV 정지 명령 전송 실패", "Error", "AGV");
        }
    }

    // 모델 클래스
    public partial class PaletteZoneModel : ObservableObject
    {
        public PaletteZoneModel(string zone, int count, int max)
        {
            ZoneName = zone;
            CurrentCount = count;
            MaxCount = max;
        }
        [ObservableProperty] private string _zoneName;
        [ObservableProperty][NotifyPropertyChangedFor(nameof(StatusText))][NotifyPropertyChangedFor(nameof(IsFull))] private int _currentCount;
        [ObservableProperty] private int _maxCount;
        public bool IsFull => CurrentCount >= MaxCount;
        public string StatusText => $"{CurrentCount} / {MaxCount}";
    }

    public class AgvHistoryModel
    {
        public DateTime Timestamp { get; set; }
        public string Zone { get; set; }
        public string Action { get; set; }
    }
}