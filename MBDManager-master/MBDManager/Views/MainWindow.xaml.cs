using MBDManager.ViewModels; // ViewModel 네임스페이스
using System; // IDisposable
using System.Windows;

namespace MBDManager.Views // 네임스페이스 확인
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // XAML에서 추가한 Closing="MainWindow_Closing" 이벤트 핸들러
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // DataContext(MainWindowViewModel)를 IDisposable로 캐스팅하여 Dispose 호출
            if (this.DataContext is IDisposable disposableViewModel)
            {
                disposableViewModel.Dispose();
            }
        }
    }
}