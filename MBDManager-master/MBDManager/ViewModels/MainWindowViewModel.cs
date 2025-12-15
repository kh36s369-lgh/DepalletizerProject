using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MBDManager.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MBDManager.ViewModels
{
    public class LogMessage
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Message { get; set; }
    }

    public partial class MainWindowViewModel : ObservableObject, IDisposable
    {
        private readonly SocketIOService _socketService;

        public static DashboardViewModel DashboardVM { get; } = new DashboardViewModel();
        public static LiveMonitoringViewModel LiveMonitoringVM { get; } = new LiveMonitoringViewModel();
        public static HistoryViewModel HistoryVM { get; } = new HistoryViewModel();
        public static DeliveryStatusViewModel DeliveryStatusVM { get; } = new DeliveryStatusViewModel();
        public static PaletteAgvViewModel PaletteAgvVM { get; } = new PaletteAgvViewModel();
        public static LogViewModel LogVM { get; } = new LogViewModel();
        public static SettingsViewModel SettingsVM { get; } = new SettingsViewModel();

        public static Action<string, string, string> GlobalLogAction;

        [ObservableProperty] private object _currentView;
        [ObservableProperty] private DateTime _currentDateTime;
        [ObservableProperty] private string _connectionStatus = "연결 대기 중...";
        [ObservableProperty] private ObservableCollection<LogMessage> _logMessages = new();
        [ObservableProperty] private string _messageToSend;

        public MainWindowViewModel()
        {
            CurrentView = DashboardVM;
            GlobalLogAction = (msg, level, device) => AddLog(msg, level, device);
            StartClock();

            // ★ [수정] 서버 주소를 설정에서 가져옴
            string url = SettingsService.Instance.Settings.ServerUrl;
            _socketService = new SocketIOService(url);

            _socketService.OnConnected += () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConnectionStatus = "🟢 서버 연결됨";
                    AddLog("서버에 연결되었습니다.", "Info", "Server");
                });
            };

            _socketService.OnDisconnected += () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ConnectionStatus = "🔴 연결 끊김";
                    AddLog("서버와의 연결이 끊어졌습니다.", "Error", "Server");
                });
            };

            _socketService.On<dynamic>("server_message", data =>
            {
                Application.Current.Dispatcher.Invoke(() => AddLog($"[Server] {data}"));
            });

            _ = _socketService.ConnectAsync();
        }

        private void StartClock()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, __) => CurrentDateTime = DateTime.Now;
            timer.Start();
        }

        private async void AddLog(string msg, string level = "Info", string device = "System")
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Insert(0, new LogMessage { Message = msg, Timestamp = DateTime.Now });
                if (LogMessages.Count > 100) LogMessages.RemoveAt(LogMessages.Count - 1);
                LogVM.AddLog(device, level, msg);
            });

            if (_socketService != null)
            {
                try { await _socketService.EmitAsync("send_log", new { level = level, message = msg, device = device }); }
                catch { }
            }
        }

        [RelayCommand]
        private void Navigate(string target)
        {
            switch (target)
            {
                case "Dashboard": CurrentView = DashboardVM; break;
                case "LiveMonitoring": CurrentView = LiveMonitoringVM; break;
                case "History": CurrentView = HistoryVM; break;
                case "DeliveryStatus": CurrentView = DeliveryStatusVM; break;
                case "PaletteAgv": CurrentView = PaletteAgvVM; break;
                case "Log": CurrentView = LogVM; break;
                case "Settings": CurrentView = SettingsVM; break;
            }
        }

        [RelayCommand]
        private async Task SendMessage()
        {
            if (string.IsNullOrWhiteSpace(MessageToSend)) return;
            var logData = new { level = "Info", message = MessageToSend };
            await _socketService.EmitAsync("send_log", logData);
            AddLog($"[보냄] {MessageToSend}");
            MessageToSend = string.Empty;
        }

        [RelayCommand]
        private void Close() { Application.Current.Shutdown(); }

        public void Dispose() { _socketService.Dispose(); }
    }
}