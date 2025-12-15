using MBDManager.Services; // SettingsService 사용을 위해 추가
using System;
using System.Windows;
using System.Windows.Controls;

namespace MBDManager.Views
{
    /// <summary>
    /// PaletteAgvView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class PaletteAgvView : UserControl
    {
        public PaletteAgvView()
        {
            InitializeComponent();

            // 화면이 로드될 때 WebView2 초기화 및 이동
            this.Loaded += PaletteAgvView_Loaded;
        }

        private async void PaletteAgvView_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. WebView2 환경 초기화 (비동기 필수)
                // 처음 실행 시 약간의 시간이 걸릴 수 있습니다.
                if (AgvWebView.CoreWebView2 == null)
                {
                    await AgvWebView.EnsureCoreWebView2Async();
                }

                // 2. 설정 파일에서 AGV IP 가져오기
                string ip = SettingsService.Instance.Settings.AgvIpAddress;
                string url = $"http://{ip}/";

                // 3. 주소 이동
                // (이미 해당 페이지에 있다면 새로고침하지 않음)
                if (AgvWebView.Source == null || AgvWebView.Source.AbsoluteUri != url)
                {
                    AgvWebView.Source = new Uri(url);
                }
            }
            catch (Exception ex)
            {
                // WebView2 런타임이 설치되지 않았거나 오류 발생 시
                System.Diagnostics.Debug.WriteLine($"WebView2 로드 오류: {ex.Message}");

                // 만약 필요하다면 MessageBox를 띄워 사용자에게 알릴 수 있습니다.
                // MessageBox.Show("WebView2 런타임 설치가 필요합니다.");
            }
        }
    }
}