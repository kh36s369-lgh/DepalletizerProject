using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MBDManager.Services; // SocketIOService가 있는 네임스페이스
using MBDManager.Views;    // LoginWindow, MainWindow가 있는 네임스페이스
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MBDManager.ViewModels
{
    // 서버에서 보내주는 로그인 응답 데이터 형태
    public class LoginResponse
    {
        public bool success { get; set; }
        public string message { get; set; }
    }

    public partial class LoginViewModel : ObservableObject, IDisposable
    {
        private readonly SocketIOService _socketService;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
        private string _username;

        [ObservableProperty]
        private string _errorMessage;

        public LoginViewModel()
        {
            // 1. Socket.IO 서비스 초기화 (Flask 기본 포트 5000)
            _socketService = new SocketIOService("http://127.0.0.1:5000");

            // 2. 서버 연결 이벤트 핸들링
            _socketService.OnConnected += () =>
            {
                // 연결 성공 시 UI 스레드에서 에러 메시지 초기화
                Application.Current.Dispatcher.Invoke(() => ErrorMessage = "");
            };

            _socketService.OnDisconnected += () =>
            {
                Application.Current.Dispatcher.Invoke(() => ErrorMessage = "서버 연결이 끊어졌습니다.");
            };

            // 3. 로그인 응답 리스너 등록 ('login_response' 이벤트 수신)
            _socketService.On<LoginResponse>("login_response", OnLoginResponse);

            // 4. 비동기 연결 시작
            _ = _socketService.ConnectAsync();
        }

        // 서버로부터 응답을 받았을 때 실행되는 메서드
        private void OnLoginResponse(LoginResponse response)
        {
            // UI 업데이트를 위해 Dispatcher 사용
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (response.success)
                {
                    // 1) 로그인 성공 메시지 (선택 사항)
                    // MessageBox.Show(response.message);

                    // 2) 메인 화면 열기
                    MainWindow mainWindow = new MainWindow();
                    mainWindow.Show();

                    // 3) 현재 로그인 창 닫기
                    CloseWindow();
                }
                else
                {
                    // 로그인 실패 메시지 표시
                    ErrorMessage = response.message;
                }
            });
        }

        [RelayCommand(CanExecute = nameof(CanLogin))]
        private async Task Login(PasswordBox passwordBox)
        {
            // 버튼을 누른 시점에 비밀번호가 비어있는지 확인
            if (passwordBox == null || string.IsNullOrEmpty(passwordBox.Password))
            {
                ErrorMessage = "비밀번호를 입력해주세요.";
                return;
            }

            try
            {
                ErrorMessage = "로그인 시도 중...";

                var loginData = new
                {
                    Username = this.Username,
                    Password = passwordBox.Password
                };

                await _socketService.EmitAsync("login_request", loginData);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"오류 발생: {ex.Message}";
            }
        }

        // 로그인 버튼 활성화 조건 (아이디가 있고 비밀번호가 입력되었을 때)
        private bool CanLogin(PasswordBox passwordBox)
        {
            return !string.IsNullOrEmpty(Username);
        }

        // 닫기 버튼 (X) 클릭 시 앱 종료
        [RelayCommand]
        private void Close()
        {
            Application.Current.Shutdown();
        }

        // 현재 활성화된 LoginWindow를 찾아 닫는 헬퍼 메서드
        private void CloseWindow()
        {
            var window = Application.Current.Windows.OfType<LoginWindow>().FirstOrDefault(w => w.DataContext == this);
            window?.Close();
        }

        // 리소스 정리
        public void Dispose()
        {
            _socketService.Dispose();
        }
    }
}