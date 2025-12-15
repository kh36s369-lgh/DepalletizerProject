using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace MBDManager.ViewModels
{
    public partial class LogViewModel : ObservableObject
    {
        // 1. 모든 로그를 담아두는 원본 리스트 (필터링 전)
        private readonly List<LogEntryModel> _allLogs = new();

        // 2. 화면에 실제 표시되는 리스트 (필터링 후)
        [ObservableProperty]
        private ObservableCollection<LogEntryModel> _logEntries = new();

        // 3. 검색 조건 바인딩 프로퍼티
        [ObservableProperty] private DateTime? _searchDate = DateTime.Now;
        [ObservableProperty] private string _selectedLevel = "전체";  // 콤보박스 선택값
        [ObservableProperty] private string _selectedDevice = "전체"; // 콤보박스 선택값

        public LogViewModel()
        {
            // 테스트용 초기 데이터 (실행 시 삭제해도 됨)
            AddLog("System", "Info", "로그 시스템 초기화 완료");
        }

        // ★★★ 외부(MainWindowViewModel 등)에서 이 함수를 호출해 로그를 추가함 ★★★
        public void AddLog(string device, string level, string message)
        {
            try
            {
                var newLog = new LogEntryModel
                {
                    Timestamp = DateTime.Now,
                    Device = device,
                    Level = level,
                    Message = message
                };

                // UI 스레드 충돌 방지를 위해 Dispatcher 사용
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // 1. 원본 저장
                    _allLogs.Insert(0, newLog);

                    // 메모리 관리: 1000개 넘으면 오래된 것 삭제
                    if (_allLogs.Count > 1000) _allLogs.RemoveAt(_allLogs.Count - 1);

                    // 2. 현재 필터 조건에 맞으면 화면 목록에도 추가
                    if (IsMatchFilter(newLog))
                    {
                        LogEntries.Insert(0, newLog);
                        if (LogEntries.Count > 500) LogEntries.RemoveAt(LogEntries.Count - 1);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log Error: {ex.Message}");
            }
        }

        // '조회' 버튼 클릭 시 실행되는 함수
        [RelayCommand]
        private void Search()
        {
            // 원본에서 조건에 맞는 것만 추려내기
            var filtered = _allLogs.Where(log => IsMatchFilter(log)).ToList();

            LogEntries.Clear();
            foreach (var item in filtered)
            {
                LogEntries.Add(item);
            }
        }

        // 필터링 로직 (날짜, 레벨, 장치 확인)
        private bool IsMatchFilter(LogEntryModel log)
        {
            // 1. 날짜 확인 (선택된 경우에만)
            if (SearchDate.HasValue && log.Timestamp.Date != SearchDate.Value.Date) return false;

            // 2. 레벨 확인
            if (SelectedLevel != "전체" && log.Level != SelectedLevel) return false;

            // 3. 장치 확인
            if (SelectedDevice != "전체" && log.Device != SelectedDevice) return false;

            return true;
        }
    }

    // 로그 데이터 모델
    public class LogEntryModel
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }   // Info, Warning, Error
        public string Device { get; set; }  // Camera, AGV, Server, System
        public string Message { get; set; }
    }
}