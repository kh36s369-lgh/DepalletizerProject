using System;

namespace MBDManager.Models
{
    public class InspectionRecord
    {
        public long Id { get; set; }
        public string Timestamp { get; set; }
        public string QrCode { get; set; }
        public string Result { get; set; }
        public string Zone { get; set; }

        // DB에서 가져오는 메인 이미지 (cam0)
        public string ImagePath { get; set; }

        public string Details { get; set; }

        public string DisplayResult => Result == "Normal" ? "정상" : "불량";

        // ★ [추가] cam0 경로를 이용해 cam1, cam2 경로를 자동으로 만듭니다.
        // 예: "..._cam0.jpg" -> "..._cam1.jpg"
        public string ImagePath1 => ImagePath?.Replace("_cam0", "_cam1");
        public string ImagePath2 => ImagePath?.Replace("_cam0", "_cam2");
    }
}