using System;
using System.Windows;
using System.Windows.Input;

// 1. 네임스페이스가 MBDManager.Views가 맞는지 확인
namespace MBDManager.Views
{
    // 2. ": Window" (Window로부터 상속)가 반드시 있어야 합니다.
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        // 창이 닫힐 때 ViewModel 정리
        private void LoginWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 3. DataContext는 Window 클래스의 속성이므로 이제 오류가 사라져야 합니다.
            if (this.DataContext is IDisposable vm)
            {
                vm.Dispose();
            }
        }

        // 창 드래그 기능 (WindowStyle="None"이므로 필요)
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

    }
}