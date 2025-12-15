using System;
using System.IO;
using System.Text.Json;

namespace MBDManager.Services
{
    // 설정값 저장용 모델
    public class AppSettings
    {
        public string ServerUrl { get; set; } = "http://127.0.0.1:5000";
        public string AgvIpAddress { get; set; } = "192.168.1.106";
        public string ArduinoPort { get; set; } = "COM3";
        public string OnnxModelPath { get; set; } = "best9.onnx";

        // 카메라 번호 (0, 1, 2...)
        public int TopCameraIndex { get; set; } = 0;
        public int SideCamera1Index { get; set; } = 1;
        public int SideCamera2Index { get; set; } = 2;
    }

    public class SettingsService
    {
        public static SettingsService Instance { get; } = new SettingsService();
        private readonly string _filePath = "config.json";
        public AppSettings Settings { get; private set; }

        private SettingsService() { Load(); }

        public void Load()
        {
            if (File.Exists(_filePath))
            {
                try { Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath)) ?? new AppSettings(); }
                catch { Settings = new AppSettings(); }
            }
            else { Settings = new AppSettings(); Save(); }
        }

        public void Save()
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}