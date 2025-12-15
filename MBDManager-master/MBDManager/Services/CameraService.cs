using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MBDManager.Services
{
    public static class CameraService
    {
        // ★ 반환 타입을 void -> Task<bool>로 변경
        public static async Task<bool> StartCapture(int cameraIndex, Dispatcher dispatcher, Action<BitmapSource> onFrame, CancellationToken token)
        {
            // 1. 카메라 초기화 (비동기 실행)
            VideoCapture capture = await Task.Run(() =>
            {
                var cap = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW);

                // 설정
                cap.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);
                cap.Set(VideoCaptureProperties.FrameWidth, 640);
                cap.Set(VideoCaptureProperties.FrameHeight, 640);
                cap.Set(VideoCaptureProperties.Fps, 15);
                cap.Set(VideoCaptureProperties.Brightness, 100);
                cap.Set(VideoCaptureProperties.Exposure, -5);

                return cap;
            });

            // 2. 카메라가 안 열렸으면 즉시 실패 반환
            if (!capture.IsOpened())
            {
                System.Diagnostics.Debug.WriteLine($"❌ 카메라 {cameraIndex}번 열기 실패");
                capture.Dispose();
                return false; // ★ 실패!
            }

            // 3. 성공했다면? -> 영상을 계속 읽어오는 무한 루프를 '별도 태스크'로 실행
            // (여기서는 await 하지 않고 던져놓음)
            _ = Task.Run(() =>
            {
                using (capture) // 캡처 객체 수명 관리
                using (var mat = new Mat())
                {
                    System.Diagnostics.Debug.WriteLine($"✅ 카메라 {cameraIndex}번 루프 시작");

                    while (!token.IsCancellationRequested)
                    {
                        if (!capture.Read(mat) || mat.Empty())
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        dispatcher.Invoke(() =>
                        {
                            try
                            {
                                var frame = mat.ToBitmapSource();
                                frame.Freeze();
                                onFrame(frame);
                            }
                            catch { }
                        });

                        Thread.Sleep(33);
                    }
                }
                System.Diagnostics.Debug.WriteLine($"카메라 {cameraIndex} 종료됨");
            }, token);

            return true; // ★ 성공!
        }
    }
}