using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MBDManager.Messages;
using MBDManager.Models;
using MBDManager.Services;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZXing;

namespace MBDManager.ViewModels
{
    public partial class LiveMonitoringViewModel : ObservableObject, IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private CancellationTokenSource _cts;
        private readonly SocketIOService _socketService;
        private SerialPort _serialPort;
        private YoloDetector _yoloDetector;

        // 팔레트 카운트 (내부 관리용)
        private int _countA = 0;
        private int _countB = 0;
        private int _countC = 0;
        private int _countD = 0;

        // UI 바인딩 프로퍼티
        [ObservableProperty] private BitmapSource _topCameraFrame;
        [ObservableProperty] private BitmapSource _sideCamera1Frame;
        [ObservableProperty] private BitmapSource _sideCamera2Frame;

        [ObservableProperty] private ObservableCollection<OnnxBoxModel> _topCameraOnnxResults = new();
        [ObservableProperty] private ObservableCollection<OnnxBoxModel> _side1OnnxResults = new();
        [ObservableProperty] private ObservableCollection<OnnxBoxModel> _side2OnnxResults = new();

        [ObservableProperty] private string _currentQrCode = "-";
        [ObservableProperty] private string _currentDefectType = "대기 중";
        [ObservableProperty] private string _currentClassification = "-";
        [ObservableProperty] private string _currentPaletteStatus = "대기 (0/4)";
        [ObservableProperty] private int _currentPaletteCount = 0;
        [ObservableProperty] private Brush _defectColor = Brushes.Gray;

        public LiveMonitoringViewModel() : this(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher) { }

        public LiveMonitoringViewModel(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _cts = new CancellationTokenSource();

            // 설정에서 서버 주소 가져오기
            string serverUrl = SettingsService.Instance.Settings.ServerUrl;
            _socketService = new SocketIOService(serverUrl);
            _ = _socketService.ConnectAsync();

            InitializeAI();
            InitializeArduino();
            StartAllCameras(); // 카메라 시작
        }

        private async void StartAllCameras()
        {
            var token = _cts.Token;
            var settings = SettingsService.Instance.Settings; // 설정 가져오기

            MainWindowViewModel.GlobalLogAction?.Invoke("카메라 연결 프로세스 시작...", "Info", "System");

            // 1번 카메라 (Side 1)
            bool isCam1Ok = await CameraService.StartCapture(settings.SideCamera1Index, _dispatcher, frame => SideCamera1Frame = frame, token);
            if (isCam1Ok) MainWindowViewModel.GlobalLogAction?.Invoke($"측면 카메라({settings.SideCamera1Index}) 연결 성공", "Info", "Camera");
            else MainWindowViewModel.GlobalLogAction?.Invoke($"측면 카메라({settings.SideCamera1Index}) 연결 실패", "Error", "Camera");

            await Task.Delay(500);

            // 2번 카메라 (Side 2)
            bool isCam2Ok = await CameraService.StartCapture(settings.SideCamera2Index, _dispatcher, frame => SideCamera2Frame = frame, token);
            if (isCam2Ok) MainWindowViewModel.GlobalLogAction?.Invoke($"측면 카메라({settings.SideCamera2Index}) 연결 성공", "Info", "Camera");
            else MainWindowViewModel.GlobalLogAction?.Invoke($"측면 카메라({settings.SideCamera2Index}) 연결 실패", "Error", "Camera");

            await Task.Delay(500);

            // 메인 카메라 (Top)
            bool isCam0Ok = await CameraService.StartCapture(settings.TopCameraIndex, _dispatcher, frame => TopCameraFrame = frame, token);
            if (isCam0Ok) MainWindowViewModel.GlobalLogAction?.Invoke($"상단 카메라({settings.TopCameraIndex}) 연결 성공", "Info", "Camera");
            else MainWindowViewModel.GlobalLogAction?.Invoke($"상단 카메라({settings.TopCameraIndex}) 연결 실패", "Error", "Camera");

            // 종합 결과 로그
            if (isCam1Ok && isCam2Ok && isCam0Ok)
                MainWindowViewModel.GlobalLogAction?.Invoke("모든 카메라 정상 작동 중", "Info", "System");
            else
                MainWindowViewModel.GlobalLogAction?.Invoke("일부 카메라 연결 실패. 설정 탭을 확인하세요.", "Warning", "System");
        }

        [RelayCommand]
        private async Task CaptureAndInspect()
        {
            if (TopCameraFrame == null)
            {
                MainWindowViewModel.GlobalLogAction?.Invoke("검사 실패: 카메라 영상 없음", "Warning", "Camera");
                return;
            }

            try
            {
                CurrentClassification = "3채널 AI 정밀 분석 중...";

                // UI 스레드에서 이미지 복제 (스냅샷)
                var frames = new BitmapSource[3];
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (TopCameraFrame != null) { frames[0] = TopCameraFrame.Clone(); frames[0].Freeze(); }
                    if (SideCamera1Frame != null) { frames[1] = SideCamera1Frame.Clone(); frames[1].Freeze(); }
                    if (SideCamera2Frame != null) { frames[2] = SideCamera2Frame.Clone(); frames[2].Freeze(); }
                });

                bool anyDefect = false;
                string finalQrCode = "Unknown";
                string mainImagePath = "";

                // 백그라운드 작업 시작
                await Task.Run(async () =>
                {
                    string dateStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    // 한글 경로 문제 방지를 위해 BaseDirectory 사용 (필요시 경로 수정 가능)
                    string saveDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InspectionData");
                    if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

                    // UI 초기화
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        TopCameraOnnxResults.Clear();
                        Side1OnnxResults.Clear();
                        Side2OnnxResults.Clear();
                    });

                    // (1) QR 찾기 (3개 카메라 중 하나라도 인식되면 성공)
                    for (int i = 0; i < 3; i++)
                    {
                        if (frames[i] == null) continue;
                        string qr = DetectQrCodeSafe(frames[i]);
                        if (!string.IsNullOrEmpty(qr))
                        {
                            finalQrCode = qr;
                            break;
                        }
                    }

                    string safeQr = finalQrCode;
                    foreach (char c in Path.GetInvalidFileNameChars()) { safeQr = safeQr.Replace(c, '_'); }

                    // (2) YOLO 추론 및 저장
                    for (int i = 0; i < 3; i++)
                    {
                        if (frames[i] == null) continue;

                        string fileName = $"{dateStr}_{safeQr}_cam{i}.jpg";
                        string rawPath = Path.Combine(saveDir, "Raw_" + fileName);
                        string resultPath = Path.Combine(saveDir, "Result_" + fileName);

                        // 2배 줌 저장 (한글 경로 대응 포함)
                        SaveZoomedImage(frames[i], rawPath, 2.0);

                        // AI 추론
                        if (_yoloDetector != null)
                        {
                            _yoloDetector.DetectAndDraw(rawPath, resultPath, out bool isDefect);
                            if (isDefect) anyDefect = true;
                        }

                        if (i == 0) mainImagePath = resultPath; // 메인 이미지 경로 저장
                    }

                    // (3) QR 미인식 시 불량 처리
                    if (finalQrCode == "Unknown" || string.IsNullOrEmpty(finalQrCode))
                        anyDefect = true;

                    // (4) 결과 정리
                    string resultType = anyDefect ? "Defect" : "Normal";
                    string targetZone = "";
                    string details = "";

                    if (anyDefect)
                    {
                        targetZone = "D";
                        details = (finalQrCode == "Unknown") ? "불량 (사유: QR 미인식)" : "불량 (사유: 외관 손상)";
                    }
                    else
                    {
                        targetZone = ParseZoneFromQR(finalQrCode);
                        details = $"정상 분류 ({targetZone}존 이동)";
                    }

                    // (5) 아두이노로 구역 정보 전송
                    if (_serialPort != null && _serialPort.IsOpen)
                    {
                        _serialPort.Write(targetZone);
                    }

                    // (6) UI 업데이트
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        CurrentQrCode = finalQrCode;

                        if (resultType == "Defect")
                        {
                            CurrentDefectType = "불량 (NG)";
                            DefectColor = Brushes.Red;
                            MainWindowViewModel.GlobalLogAction?.Invoke($"불량 발생! ({details})", "Warning", "ONNX");
                        }
                        else
                        {
                            CurrentDefectType = "정상 (OK)";
                            DefectColor = Brushes.LimeGreen;
                            MainWindowViewModel.GlobalLogAction?.Invoke($"정상 처리 완료 ({finalQrCode})", "Info", "System");
                        }

                        CurrentClassification = $"검사 완료 ({targetZone}구역)";

                        // 팔레트 카운팅 로직
                        UpdatePaletteCount(targetZone);
                    });

                    // (7) 데이터 전송 (히스토리 & 서버)
                    var newRecord = new InspectionRecord
                    {
                        Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        QrCode = finalQrCode,
                        Result = resultType,
                        Zone = targetZone + "-Zone",
                        ImagePath = mainImagePath,
                        Details = details
                    };

                    WeakReferenceMessenger.Default.Send(new NewInspectionMessage(newRecord));

                    await _socketService.EmitAsync("send_inspection", new
                    {
                        qr_code = finalQrCode,
                        result = resultType,
                        zone = targetZone,
                        image_path = mainImagePath
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"통합 검사 오류: {ex.Message}");
                CurrentClassification = "오류 발생";
                MainWindowViewModel.GlobalLogAction?.Invoke($"검사 로직 오류: {ex.Message}", "Error", "System");
            }
        }

        private void UpdatePaletteCount(string targetZone)
        {
            int currentCount = 0;
            switch (targetZone)
            {
                case "A": _countA++; currentCount = _countA; break;
                case "B": _countB++; currentCount = _countB; break;
                case "C": _countC++; currentCount = _countC; break;
                case "D": _countD++; currentCount = _countD; break;
            }

            WeakReferenceMessenger.Default.Send(new PaletteUpdateMessage((targetZone, currentCount)));

            if (currentCount >= 4)
            {
                CurrentPaletteStatus = $"{targetZone}구역 Full! (AGV 호출 🚨)";
                CurrentPaletteCount = 4;

                // 카운터 리셋
                if (targetZone == "A") _countA = 0;
                else if (targetZone == "B") _countB = 0;
                else if (targetZone == "C") _countC = 0;
                else if (targetZone == "D") _countD = 0;

                // 3초 뒤 UI 초기화 메시지 전송
                Task.Delay(3000).ContinueWith(_ => WeakReferenceMessenger.Default.Send(new PaletteUpdateMessage((targetZone, 0))));
            }
            else
            {
                CurrentPaletteStatus = $"{targetZone}구역 적재 중 ({currentCount}/4)";
                CurrentPaletteCount = currentCount;
            }
        }

        // --- 유틸리티 메서드들 (생략 없이 전체 포함) ---

        // QR 인식 (줌 -> 흑백 -> 반전 -> 이진화 시도)
        private string DetectQrCodeSafe(BitmapSource image)
        {
            try
            {
                using var mat = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToMat(image);

                // 1. 디지털 줌 (2배)
                using var zoomedMat = new Mat();
                Cv2.Resize(mat, zoomedMat, new OpenCvSharp.Size(mat.Cols * 2, mat.Rows * 2), 0, 0, InterpolationFlags.Cubic);

                // 2. 흑백 변환
                using var grayMat = new Mat();
                Cv2.CvtColor(zoomedMat, grayMat, ColorConversionCodes.BGR2GRAY);

                // [시도 1] 기본 흑백
                string result = TryDecode(grayMat);
                if (!string.IsNullOrEmpty(result)) return result;

                // [시도 2] 색상 반전 (검은 배경 흰 QR 대응)
                using var invertedMat = new Mat();
                Cv2.BitwiseNot(grayMat, invertedMat);
                result = TryDecode(invertedMat);
                if (!string.IsNullOrEmpty(result)) return result;

                // [시도 3] 적응형 이진화 (그림자/조명 대응)
                using var adaptiveMat = new Mat();
                Cv2.AdaptiveThreshold(grayMat, adaptiveMat, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 25, 10);
                result = TryDecode(adaptiveMat);

                return result;
            }
            catch { return null; }
        }

        // QR 디코딩 (ZXing)
        private string TryDecode(Mat matImage)
        {
            try
            {
                byte[] bytes;
                Cv2.ImEncode(".bmp", matImage, out bytes);
                using var ms = new MemoryStream(bytes);
                using var bitmap = new System.Drawing.Bitmap(ms);

                var reader = new ZXing.Windows.Compatibility.BarcodeReader
                {
                    AutoRotate = true,
                    Options = new ZXing.Common.DecodingOptions { TryHarder = true }
                };
                var result = reader.Decode(bitmap);
                return result?.Text;
            }
            catch { return null; }
        }

        // 이미지 저장 (줌 + 한글 경로 문제 해결 버전)
        private void SaveZoomedImage(BitmapSource image, string filePath, double zoomFactor)
        {
            try
            {
                // 1. 디렉토리 확인
                string dir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using var mat = OpenCvSharp.WpfExtensions.BitmapSourceConverter.ToMat(image);
                using var finalMat = new Mat();

                // 줌(Crop & Resize) 처리
                if (zoomFactor > 1.0)
                {
                    int w = mat.Cols;
                    int h = mat.Rows;
                    int cropW = (int)(w / zoomFactor);
                    int cropH = (int)(h / zoomFactor);
                    int x = (w - cropW) / 2;
                    int y = (h - cropH) / 2;

                    // 범위 체크
                    if (x < 0) x = 0; if (y < 0) y = 0;
                    if (cropW > w) cropW = w; if (cropH > h) cropH = h;

                    using var crop = new Mat(mat, new OpenCvSharp.Rect(x, y, cropW, cropH));
                    Cv2.Resize(crop, finalMat, new OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Cubic);
                }
                else
                {
                    mat.CopyTo(finalMat);
                }

                // ★ 한글 경로 저장을 위해 FileStream 사용
                bool success = Cv2.ImEncode(".jpg", finalMat, out byte[] buf);
                if (success)
                {
                    File.WriteAllBytes(filePath, buf);
                }
                else
                {
                    // OpenCV 인코딩 실패 시 WPF 인코더 사용 (백업)
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        BitmapEncoder encoder = new JpegBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(image));
                        encoder.Save(fileStream);
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.GlobalLogAction?.Invoke($"이미지 저장 실패: {ex.Message}", "Error", "System");
            }
        }

        private string ParseZoneFromQR(string qrCode)
        {
            if (string.IsNullOrEmpty(qrCode) || qrCode == "Unknown") return "C";
            if (qrCode.Contains("서울")) return "A";
            if (qrCode.Contains("부산")) return "B";
            if (qrCode.Contains("천안")) return "C";
            return "C";
        }

        private void InitializeArduino()
        {
            try
            {
                // 설정에서 포트 가져오기
                string port = SettingsService.Instance.Settings.ArduinoPort;
                _serialPort = new SerialPort(port, 9600);

                // ★★★ [수정됨] 이벤트 핸들러를 별도 함수로 연결 ★★★
                _serialPort.DataReceived += OnArduinoDataReceived;

                _serialPort.Open();
                MainWindowViewModel.GlobalLogAction?.Invoke($"아두이노(Conveyor) 연결 성공 ({port})", "Info", "System");
            }
            catch (Exception ex)
            {
                MainWindowViewModel.GlobalLogAction?.Invoke($"아두이노 연결 실패: {ex.Message}", "Error", "System");
            }
        }
        // ★★★ [신규 추가] 아두이노 신호 수신 함수 ★★★
        private void OnArduinoDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    // 1. 아두이노가 보낸 메시지 읽기 (예: "REQ_INSPECT")
                    string message = _serialPort.ReadLine().Trim();

                    // (디버깅용 로그 - 필요시 주석 해제)
                    // System.Diagnostics.Debug.WriteLine($"[Arduino Received] {message}");

                    // 2. 메시지가 "REQ_INSPECT" (검사 요청) 이라면?
                    // (아두이노 코드에서 Serial.println("REQ_INSPECT"); 하는 부분과 일치해야 함)
                    if (message.Contains("REQ_INSPECT"))
                    {
                        // 3. UI 스레드에서 검사 함수 실행!
                        Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            // ★ 로그 남기기
                            MainWindowViewModel.GlobalLogAction?.Invoke("로드셀 감지 -> 자동 검사 시작", "Info", "System");
                            await CaptureAndInspect();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"시리얼 수신 오류: {ex.Message}");
            }
        }
        private void InitializeAI()
        {
            try
            {
                // 설정에서 모델 경로 가져오기
                string modelName = SettingsService.Instance.Settings.OnnxModelPath;
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelName);
                if (File.Exists(fullPath))
                {
                    _yoloDetector = new YoloDetector(fullPath);
                    MainWindowViewModel.GlobalLogAction?.Invoke($"AI 모델({modelName}) 로드 완료", "Info", "ONNX");
                }
                else
                {
                    MainWindowViewModel.GlobalLogAction?.Invoke($"AI 모델을 찾을 수 없음: {modelName}", "Error", "ONNX");
                }
            }
            catch (Exception ex)
            {
                MainWindowViewModel.GlobalLogAction?.Invoke($"AI 초기화 오류: {ex.Message}", "Error", "ONNX");
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _socketService?.Dispose();
            _yoloDetector?.Dispose();
            if (_serialPort != null && _serialPort.IsOpen) _serialPort.Close();
        }
    }
}