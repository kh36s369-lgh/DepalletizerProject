using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MBDManager.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace MBDManager.ViewModels
{
    public partial class DeliveryStatusViewModel : ObservableObject, IDisposable
    {
        private readonly SocketIOService _socketService;

        [ObservableProperty] private DateTime _selectedDate = DateTime.Now;
        [ObservableProperty] private string _searchTrackingNumber;
        [ObservableProperty] private int _selectedDateTotalCount;
        [ObservableProperty] private ObservableCollection<DeliveryPackageModel> _dailyPackages = new();
        [ObservableProperty] private DeliveryPackageModel _selectedPackage;

        public DeliveryStatusViewModel()
        {
            _socketService = new SocketIOService("http://127.0.0.1:5000");
            // 서버 응답 리스너
            _socketService.On<List<dynamic>>("search_delivery_response", OnSearchResponse);
            _ = _socketService.ConnectAsync();

            // 초기 로딩
            _ = Search();
        }

        partial void OnSelectedDateChanged(DateTime value) { _ = Search(); }

        [RelayCommand] private async Task SearchByTrackingNumber() { await Search(); }

        private async Task Search()
        {
            var filter = new
            {
                date = SelectedDate.ToString("yyyy-MM-dd"),
                tracking_number = SearchTrackingNumber
            };
            await _socketService.EmitAsync("search_delivery", filter);
        }

        private void OnSearchResponse(List<dynamic> data)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DailyPackages.Clear();
                foreach (var item in data)
                {
                    // JSON 파싱 헬퍼
                    string GetString(object obj, string propName)
                    {
                        try
                        {
                            if (obj is JsonElement element)
                            {
                                if (element.TryGetProperty(propName, out var prop)) return prop.ToString();
                            }
                            else return ((dynamic)obj)[propName]?.ToString();
                        }
                        catch { }
                        return "-";
                    }

                    string tsStr = GetString(item, "timestamp");
                    DateTime.TryParse(tsStr, out DateTime inspectTime);

                    // ★ 서버 데이터 파싱 (app.py에서 보낸 키값과 일치해야 함)
                    string fullAddress = GetString(item, "address");
                    string sender = GetString(item, "sender_name");
                    string receiver = GetString(item, "receiver_name"); // 받는 사람
                    string itemName = GetString(item, "item_name");
                    string status = GetString(item, "result");
                    string qr = GetString(item, "qr_code");

                    // ★ 주소 앞 2글자로 지역(Zone) 설정
                    string regionZone = "기타";
                    if (!string.IsNullOrEmpty(fullAddress) && fullAddress.Length >= 2)
                    {
                        regionZone = fullAddress.Substring(0, 2);
                    }

                    var package = new DeliveryPackageModel
                    {
                        Timestamp = inspectTime.ToString("HH:mm:ss"),
                        TrackingNumber = qr,
                        CurrentStatus = status,
                        Zone = regionZone, // 서울, 제주 등

                        SenderName = sender,
                        ReceiverName = receiver,
                        ReceiverAddress = fullAddress,
                        ItemName = itemName,

                        InspectionTime = inspectTime
                    };

                    package.GenerateSimulationSteps();
                    DailyPackages.Add(package);
                }
                SelectedDateTotalCount = DailyPackages.Count;
            });
        }

        public void Dispose() { _socketService.Dispose(); }
    }

    // --- UI 표현용 모델 클래스 ---
    public partial class DeliveryPackageModel : ObservableObject
    {
        [ObservableProperty] private string _timestamp;
        [ObservableProperty] private string _trackingNumber;
        [ObservableProperty] private string _currentStatus;
        [ObservableProperty] private string _zone; // 지역명

        // 상세 정보
        [ObservableProperty] private string _senderName;
        [ObservableProperty] private string _receiverName;
        [ObservableProperty] private string _receiverAddress;
        [ObservableProperty] private string _itemName;

        public DateTime InspectionTime { get; set; }

        [ObservableProperty] private ObservableCollection<StepModel> _statusSteps;

        // 타임라인 생성 로직
        public void GenerateSimulationSteps()
        {
            StatusSteps = new ObservableCollection<StepModel>();

            StatusSteps.Add(new StepModel($"택배 접수 ({SenderName}님)", InspectionTime.AddMinutes(-10), true));

            if (CurrentStatus == "접수 완료")
            {
                StatusSteps.Add(new StepModel("스마트 검수 대기", null, false));
                StatusSteps.Add(new StepModel($"터미널 이동 ({Zone})", null, false));
            }
            else if (CurrentStatus == "배송 중" || CurrentStatus == "Normal")
            {
                StatusSteps.Add(new StepModel("AI 스마트 검수 (정상)", InspectionTime, true));
                StatusSteps.Add(new StepModel($"지역 분류 완료 ({Zone})", InspectionTime.AddMinutes(2), true));
                StatusSteps.Add(new StepModel("배송 기사 인계", InspectionTime.AddMinutes(30), true));
                StatusSteps.Add(new StepModel("배송 중", InspectionTime.AddHours(1), true));
            }
            else if (CurrentStatus == "반송/불량 대기" || CurrentStatus == "Defect")
            {
                StatusSteps.Add(new StepModel("AI 스마트 검수 (불량)", InspectionTime, true));
                StatusSteps.Add(new StepModel("불량 분류 (D구역)", InspectionTime.AddMinutes(2), true));
                StatusSteps.Add(new StepModel("반송 처리 대기", null, false));
            }
            else
            {
                StatusSteps.Add(new StepModel(CurrentStatus, InspectionTime, true));
            }
        }
    }

    public partial class StepModel : ObservableObject
    {
        public StepModel(string stepName, DateTime? time, bool completed)
        {
            StepName = stepName;
            Timestamp = time;
            IsCompleted = completed;
        }

        [ObservableProperty] private string _stepName;
        [ObservableProperty] private DateTime? _timestamp;
        [ObservableProperty] private bool _isCompleted;
    }
}