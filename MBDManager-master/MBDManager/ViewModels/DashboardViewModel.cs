using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MBDManager.Messages;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using MBDManager.Services;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace MBDManager.ViewModels
{
    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        private readonly SocketIOService _socketService;
        private readonly DispatcherTimer _refreshTimer;

        [ObservableProperty] private string _masterStatusText = "시스템 대기 중";
        [ObservableProperty] private string _masterStatusColor = "Gray";
        [ObservableProperty] private DashboardSummaryModel _summary = new();
        [ObservableProperty] private ObservableCollection<PaletteStatusModel> _paletteStatuses;
        [ObservableProperty] private ObservableCollection<AlarmItemModel> _activeAlarms = new();
        [ObservableProperty] private ISeries[] _donutSeries1;
        [ObservableProperty] private ISeries[] _donutSeries2;
        [ObservableProperty] private ISeries[] _donutSeries3;

        public DashboardViewModel()
        {
            PaletteStatuses = new ObservableCollection<PaletteStatusModel>
            {
                new PaletteStatusModel { ZoneName = "A 구역", CurrentCount = 0, MaxCount = 4 },
                new PaletteStatusModel { ZoneName = "B 구역", CurrentCount = 0, MaxCount = 4 },
                new PaletteStatusModel { ZoneName = "C 구역", CurrentCount = 0, MaxCount = 4 },
                new PaletteStatusModel { ZoneName = "D 구역", CurrentCount = 0, MaxCount = 4 }
            };

            DonutSeries1 = new ISeries[] { };
            DonutSeries2 = new ISeries[] { };
            DonutSeries3 = new ISeries[] { };

            WeakReferenceMessenger.Default.Register<PaletteUpdateMessage>(this, (r, m) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var target = PaletteStatuses.FirstOrDefault(p => p.ZoneName.Replace(" ", "").Contains(m.Value.Zone));
                    if (target != null)
                    {
                        target.CurrentCount = m.Value.Count;
                    }
                });
            });

            _socketService = new SocketIOService("http://127.0.0.1:5000");
            _socketService.On<dynamic>("dashboard_stats_response", OnStatsReceived);
            _ = _socketService.ConnectAsync();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _refreshTimer.Tick += async (s, e) => await RequestStats();
            _refreshTimer.Start();
            _ = RequestStats();
        }

        private async Task RequestStats() { await _socketService.EmitAsync("get_dashboard_stats", new { }); }

        private void OnStatsReceived(dynamic data)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var sumData = data.GetProperty("summary");

                    int registered = sumData.GetProperty("registered").GetInt32();
                    int inspected = sumData.GetProperty("inspected").GetInt32();
                    int normal = sumData.GetProperty("normal").GetInt32();
                    int defect = sumData.GetProperty("defect").GetInt32();

                    // 음수 방지 및 데이터 보정 로직
                    int waiting = registered - inspected;
                    if (waiting < 0) waiting = 0;
                    if (registered < inspected) registered = inspected;

                    Summary.TotalCount = registered;
                    Summary.NormalCount = normal;
                    Summary.DefectCount = defect;

                    double defectRate = inspected > 0 ? (double)defect / inspected : 0;
                    if (defectRate > 0.1)
                    {
                        MasterStatusText = "품질 경고 (불량↑)"; MasterStatusColor = "Orange";
                    }
                    else
                    {
                        MasterStatusText = "시스템 정상 가동"; MasterStatusColor = "Green";
                    }

                    // ★★★ [수정] 폰트를 Pretendard로 변경 ★★★
                    var labelPaint = new SolidColorPaint(SKColors.White)
                    {
                        // 윈도우에 설치된 폰트 이름 "Pretendard"를 사용
                        SKTypeface = SKTypeface.FromFamilyName("Pretendard")
                    };

                    // ★ [차트 1] 총 물량
                    DonutSeries1 = new ISeries[]
                    {
                        new PieSeries<int>
                        {
                            Values = new[] { inspected },
                            Name = "완료",
                            Fill = new SolidColorPaint(SKColors.RoyalBlue),
                            InnerRadius = 50,
                            DataLabelsPaint = labelPaint, // Pretendard 적용
                            DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                            DataLabelsFormatter = point => $"{point.Context.Series.Name}: {point.PrimaryValue}"
                        },
                        new PieSeries<int>
                        {
                            Values = new[] { waiting },
                            Name = "대기",
                            Fill = new SolidColorPaint(SKColors.LightGray),
                            InnerRadius = 50,
                            DataLabelsPaint = labelPaint, // Pretendard 적용
                            DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                            DataLabelsFormatter = point => point.PrimaryValue > 0 ? $"{point.Context.Series.Name}: {point.PrimaryValue}" : ""
                        }
                    };

                    // ★ [차트 2] 구역별 물량
                    var zonesData = data.GetProperty("zones");
                    var zoneSeries = new List<ISeries>();

                    AddZoneSeries(zoneSeries, "A", SKColors.MediumTurquoise, zonesData);
                    AddZoneSeries(zoneSeries, "B", SKColors.Salmon, zonesData);
                    AddZoneSeries(zoneSeries, "C", SKColors.MediumPurple, zonesData);
                    AddZoneSeries(zoneSeries, "D", SKColors.Gold, zonesData);

                    DonutSeries2 = zoneSeries.ToArray();

                    // ★ [차트 3] 불량률
                    DonutSeries3 = new ISeries[]
                    {
                        new PieSeries<int>
                        {
                            Values = new[] { normal },
                            Name = "정상",
                            Fill = new SolidColorPaint(SKColors.SeaGreen),
                            InnerRadius = 50,
                            DataLabelsPaint = labelPaint, // Pretendard 적용
                            DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                            DataLabelsFormatter = point => point.PrimaryValue > 0 ? $"{point.PrimaryValue}건" : ""
                        },
                        new PieSeries<int>
                        {
                            Values = new[] { defect },
                            Name = "불량",
                            Fill = new SolidColorPaint(SKColors.Crimson),
                            InnerRadius = 50,
                            DataLabelsPaint = labelPaint, // Pretendard 적용
                            DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                            DataLabelsFormatter = point => point.PrimaryValue > 0 ? $"{point.PrimaryValue}건" : ""
                        }
                    };

                    ActiveAlarms.Clear();
                    var alarmList = data.GetProperty("alarms");
                    foreach (var alarm in alarmList.EnumerateArray())
                    {
                        ActiveAlarms.Add(new AlarmItemModel { Timestamp = alarm.GetProperty("timestamp").ToString(), DeviceName = "System Log", Message = $"[{alarm.GetProperty("level")}] {alarm.GetProperty("message")}" });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Dashboard Update Error: {ex.Message}");
                }
            });
        }

        private void AddZoneSeries(List<ISeries> series, string key, SKColor color, dynamic zonesData)
        {
            try
            {
                if (((JsonElement)zonesData).TryGetProperty(key, out JsonElement val))
                {
                    int count = val.GetInt32();
                    if (count > 0)
                    {
                        series.Add(new PieSeries<int>
                        {
                            Values = new[] { count },
                            Name = $"{key} 구역",
                            Fill = new SolidColorPaint(color),
                            InnerRadius = 50,
                            // ★★★ [수정] 여기도 Pretendard 적용 ★★★
                            DataLabelsPaint = new SolidColorPaint(SKColors.White)
                            {
                                SKTypeface = SKTypeface.FromFamilyName("Pretendard")
                            },
                            DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                            DataLabelsFormatter = point => $"{key}: {point.PrimaryValue}"
                        });
                    }
                }
            }
            catch { }
        }

        public void Dispose() { _refreshTimer?.Stop(); _socketService.Dispose(); }
    }

    public partial class DashboardSummaryModel : ObservableObject
    {
        [ObservableProperty] private int _totalCount;
        [ObservableProperty] private int _normalCount;
        [ObservableProperty] private int _defectCount;
    }

    public partial class PaletteStatusModel : ObservableObject
    {
        [ObservableProperty] private string _zoneName;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        [NotifyPropertyChangedFor(nameof(IsFull))]
        private int _currentCount;
        [ObservableProperty] private int _maxCount = 4;

        public string StatusText => $"{CurrentCount} / {MaxCount}";
        public bool IsFull => CurrentCount >= MaxCount;
    }

    public class AlarmItemModel
    {
        public string DeviceName { get; set; }
        public string Message { get; set; }
        public string Timestamp { get; set; }
    }
}