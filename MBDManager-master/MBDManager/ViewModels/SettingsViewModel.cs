using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MBDManager.Services;
using System.Windows;

namespace MBDManager.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        // View에서 바인딩할 설정 객체
        [ObservableProperty]
        private AppSettings _currentSettings;

        public SettingsViewModel()
        {
            // 프로그램 켜질 때, 파일에서 불러온 설정값을 화면에 표시
            CurrentSettings = SettingsService.Instance.Settings;
        }

        [RelayCommand]
        private void SaveSettings()
        {
            // 1. 파일로 저장
            SettingsService.Instance.Save();

            // 2. 안내 메시지
            MessageBox.Show("설정이 저장되었습니다.\n변경 사항을 적용하려면 프로그램을 재시작해주세요.",
                            "설정 저장 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}