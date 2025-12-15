#include <HX711.h> 
#include <Wire.h>
#include <LiquidCrystal_I2C.h>
#include <Servo.h>
#include <math.h>   // fabs 사용

// ======================= HX711 & LCD 설정 =======================

const int DOUT_PIN = 8;   // HX711 DT
const int SCK_PIN  = 9;   // HX711 SCK

HX711 scale;
float calibration_factor = -1998.0;

LiquidCrystal_I2C lcd(0x27, 16, 2);

int weightState = 1;   // 1: 대기, 2: 측정+통신대기+이동, 3: 제거 대기

const float ZERO_THRESHOLD      = 2.0;  // 이 값 미만이면 "없다"
const float WEIGHING_THRESHOLD  = 3.0;  // 이 값 이상이면 "물체 있다"

// ======================= 적재 수량 관리 (추가됨) =======================
// 각 구역별 현재 적재된 수량 (0 ~ 4)
// 인덱스: 0=A, 1=B, 2=C, 3=D
int zoneCounts[4] = {0, 0, 0, 0};

// ======================= 서보 핀 =======================

Servo sBase, sShoulder, sElbow, sWristP, sWristR, sGrip;

#define BASE_PIN      2
#define SHOULDER_PIN  3
#define ELBOW_PIN     4
#define WRIST_P_PIN   5
#define WRIST_R_PIN   6
#define GRIP_PIN      7

// ======================= Grip 설정 =======================

const int GRIP_OPEN = 25;
const int GRIP_HOLD = 45;

bool gripAttached = false;



// ======================= Pose 구조체 =======================

struct Pose {
  int b, s, e, wp, wr, g;
};

// ======================= 기본 자세 / 픽업 자세 =======================

const Pose HOME      = {7, 140, 115, 105, 88, GRIP_OPEN};

const Pose PICK_ABOVE     = {7, 140, 115, 105, 88, GRIP_OPEN};
const Pose PICK_DOWN      = {7, 145, 140, 130, 92, GRIP_OPEN};
const Pose LIFT_FROM_PICK = {7, 140, 115, 105, 88, GRIP_HOLD};
const Pose CARRY_MID      = {7, 130, 100, 105, 88, GRIP_HOLD};

// ======================= 팔레트 슬롯 Pose =======================
// A 구역
const Pose A_ABOVE[4] = {
  {70, 130, 95, 105, 88, GRIP_HOLD}, {70, 130, 95, 105, 88, GRIP_HOLD},
  {70, 130, 95, 105, 88, GRIP_HOLD}, {70, 130, 95, 105, 88, GRIP_HOLD}
};
const Pose A_TOUCH[4] = {
  {80, 150, 160, 140, 88, GRIP_HOLD}, {60, 150, 160, 130, 88, GRIP_HOLD},
  {68, 130, 160,  95, 88, GRIP_HOLD}, {85, 140, 145, 135, 88, GRIP_HOLD}
};
const Pose A_LIFT[4] = {
  {70, 135, 100, 100, 88, GRIP_OPEN}, {70, 135, 100, 100, 88, GRIP_OPEN},
  {70, 135, 100, 100, 88, GRIP_OPEN}, {70, 135, 100, 100, 88, GRIP_OPEN}
};

// B 구역
const Pose B_ABOVE[4] = {
  {110, 130, 95, 105, 88, GRIP_HOLD}, {110, 130, 95, 105, 88, GRIP_HOLD},
  {110, 130, 95, 105, 88, GRIP_HOLD}, {110, 130, 95, 105, 88, GRIP_HOLD}
};
const Pose B_TOUCH[4] = {
  {135, 145, 160, 145, 88, GRIP_HOLD}, {115, 145, 160, 150, 88, GRIP_HOLD},
  {115, 140, 165, 130, 88, GRIP_HOLD}, {125, 145, 160, 125, 88, GRIP_HOLD}
};
const Pose B_LIFT[4] = {
  {110, 135, 100, 100, 88, GRIP_OPEN}, {110, 135, 100, 100, 88, GRIP_OPEN},
  {110, 135, 100, 100, 88, GRIP_OPEN}, {110, 135, 100, 100, 88, GRIP_OPEN}
};

// C 구역
const Pose C_ABOVE[4] = {
  {150, 130, 95, 105, 88, GRIP_HOLD}, {150, 130, 95, 105, 88, GRIP_HOLD},
  {150, 130, 95, 105, 88, GRIP_HOLD}, {150, 130, 95, 105, 88, GRIP_HOLD}
};
const Pose C_TOUCH[4] = {
  {158, 130, 138, 120, 88, GRIP_HOLD}, {150, 150, 160, 130, 88, GRIP_HOLD},
  {145, 140, 160, 100, 88, GRIP_HOLD}, {148, 140, 160,  90, 88, GRIP_HOLD}
};
const Pose C_LIFT[4] = {
  {150, 135, 100, 100, 88, GRIP_OPEN}, {150, 135, 100, 100, 88, GRIP_OPEN},
  {150, 135, 100, 100, 88, GRIP_OPEN}, {150, 135, 100, 100, 88, GRIP_OPEN}
};

// D 구역
const Pose D_ABOVE[4] = {
  {180, 130, 95, 105, 88, GRIP_HOLD}, {180, 130, 95, 105, 88, GRIP_HOLD},
  {180, 130, 95, 105, 88, GRIP_HOLD}, {180, 130, 95, 105, 88, GRIP_HOLD}
};
const Pose D_TOUCH[4] = {
  {180, 158, 138, 130, 88, GRIP_HOLD}, {180, 158, 138, 130, 88, GRIP_HOLD},
  {180, 158, 138, 130, 88, GRIP_HOLD}, {180, 158, 138, 130, 88, GRIP_HOLD}
};
const Pose D_LIFT[4] = {
  {180, 135, 100, 100, 88, GRIP_OPEN}, {180, 135, 100, 100, 88, GRIP_OPEN},
  {180, 135, 100, 100, 88, GRIP_OPEN}, {180, 135, 100, 100, 88, GRIP_OPEN}
};

// ======================= 각도 제한 =======================

const int MIN_A[6] = {0, 40, 40, 70, 80, 30};
const int MAX_A[6] = {180, 170, 160, 160, 120, 120};

bool isPaused = false;

// ======================= 유틸 =======================

int clampAngle(int v, int lo, int hi) {
  if (v < lo) return lo;
  if (v > hi) return hi;
  return v;
}

void attachGripIfNeeded() {
  if (!gripAttached) {
    sGrip.attach(GRIP_PIN);
    sGrip.write(GRIP_OPEN);
    gripAttached = true;
    delay(200);
  }
}

void detachGrip() {
  if (gripAttached) {
    sGrip.detach();
    gripAttached = false;
  }
}

void writePose(const Pose& p) {
  sBase.write(    clampAngle(p.b,  MIN_A[0], MAX_A[0]) );
  sShoulder.write(clampAngle(p.s,  MIN_A[1], MAX_A[1]) );
  sElbow.write(   clampAngle(p.e,  MIN_A[2], MAX_A[2]) );
  sWristP.write(  clampAngle(p.wp, MIN_A[3], MAX_A[3]) );
  sWristR.write(  clampAngle(p.wr, MIN_A[4], MAX_A[4]) );

  if (gripAttached) {
    sGrip.write(  clampAngle(p.g,  MIN_A[5], MAX_A[5]) );
  }
}

Pose readPose() {
  Pose p = {
    sBase.read(), sShoulder.read(), sElbow.read(),
    sWristP.read(), sWristR.read(),
    gripAttached ? sGrip.read() : GRIP_OPEN
  };
  return p;
}

void checkSerial() {
  while (Serial.available() > 0) {
    char c = Serial.read();
    if (c == 'q' || c == 'Q') {
      isPaused = true;
      Serial.println("STOPPED.");
    }
    else if (c == 'r' || c == 'R') {
      isPaused = false;
      Serial.println("RESUMED.");
    }
  }
}

void waitPause() {
  while (isPaused) {
    if (Serial.available() > 0) {
        char c = Serial.read();
        if (c == 'r' || c == 'R') {
            isPaused = false;
            Serial.println("RESUMED.");
        }
    }
    delay(10);
  }
}

void safeDelay(unsigned long ms) {
  unsigned long start = millis();
  while (millis() - start < ms) {
    checkSerial();
    waitPause();
    delay(5);
  }
}

void moveSmooth(const Pose& tgt, int delayMs) {
  Pose cur = readPose();
  while (cur.b != tgt.b || cur.s != tgt.s || cur.e != tgt.e ||
         cur.wp != tgt.wp || cur.wr != tgt.wr || cur.g != tgt.g) {
    checkSerial();
    waitPause();

    if (cur.b  < tgt.b)  cur.b++; else if (cur.b  > tgt.b)  cur.b--;
    if (cur.s  < tgt.s)  cur.s++; else if (cur.s  > tgt.s)  cur.s--;
    if (cur.e  < tgt.e)  cur.e++; else if (cur.e  > tgt.e)  cur.e--;
    if (cur.wp < tgt.wp) cur.wp++; else if (cur.wp > tgt.wp) cur.wp--;
    if (cur.wr < tgt.wr) cur.wr++; else if (cur.wr > tgt.wr) cur.wr--;
    if (cur.g  < tgt.g)  cur.g++; else if (cur.g  > tgt.g)  cur.g--;

    writePose(cur);
    safeDelay(delayMs);
  }
}

// ======================= Pick / Place =======================

void pickFromHome() {
  attachGripIfNeeded();
  moveSmooth(HOME, 15);
  safeDelay(200);
  moveSmooth(PICK_ABOVE, 15);
  safeDelay(100);
  moveSmooth(PICK_DOWN, 25);
  safeDelay(200);

  Pose cur = readPose();
  cur.g = GRIP_HOLD;
  moveSmooth(cur, 15);
  safeDelay(100);

  moveSmooth(LIFT_FROM_PICK, 15);
  safeDelay(80);
  moveSmooth(CARRY_MID, 15);
  safeDelay(80);
}

void placeToSlot(const Pose above[4], const Pose touch[4], const Pose lift[4], int slotIndex) {
  if (slotIndex < 0) slotIndex = 0;
  if (slotIndex > 3) slotIndex = 3;

  moveSmooth(above[slotIndex], 15);
  safeDelay(100);
  moveSmooth(touch[slotIndex], 25);
  safeDelay(150);

  Pose cur = readPose();
  cur.g = GRIP_OPEN;
  moveSmooth(cur, 15);
  safeDelay(150);

  moveSmooth(lift[slotIndex], 15);
  safeDelay(80);
  moveSmooth(HOME, 15);
  safeDelay(150);
  detachGrip();
}

// ======================= Setup =======================

void setup() {
  Serial.begin(9600);
  Serial.println("System Ready. Waiting for Weight...");

  sBase.attach(BASE_PIN);
  sShoulder.attach(SHOULDER_PIN);
  sElbow.attach(ELBOW_PIN);
  sWristP.attach(WRIST_P_PIN);
  sWristR.attach(WRIST_R_PIN);
  
  attachGripIfNeeded();      
  writePose(HOME);
  safeDelay(1000);
  detachGrip();              

  lcd.init();
  lcd.backlight();
  lcd.clear();
  lcd.print("Calibrating...");

  scale.begin(DOUT_PIN, SCK_PIN);
  scale.set_scale(calibration_factor);
  safeDelay(2000);
  scale.tare();

  Serial.println("Zeroing Complete.");
  lcd.clear();
  lcd.print("Ready...");
  lcd.setCursor(0,1);
  lcd.print("Put object");
}

// ======================= Loop =======================

void loop() {
  checkSerial();
  waitPause();

  float currentWeight = scale.get_units(5);

  // -------- 상태 1 : 대기 --------
  if (weightState == 1) {
    if (currentWeight > WEIGHING_THRESHOLD) {
      lcd.clear();
      lcd.print("Weighing...");
      weightState = 2;
    }
  }

  // -------- 상태 2 : 측정 -> 통신 -> 이동 -> (Full체크) --------
  else if (weightState == 2) {
    safeDelay(3000); // 3초 안정화

    float m1 = scale.get_units(10);
    safeDelay(200);
    float m2 = scale.get_units(10);
    safeDelay(200);
    float m3 = scale.get_units(10);
    float avg = (m1 + m2 + m3) / 3.0;

    lcd.clear();
    lcd.setCursor(0,0);
    lcd.print("W: "); lcd.print(avg, 2);
    lcd.setCursor(0,1);
    lcd.print("Req Inspection..");

    // 1. PC에 검사 요청
    Serial.println("REQ_INSPECT"); 
    
    // 2. PC 응답 대기
    char targetZone = 0;
    while (targetZone == 0) {
        if (Serial.available() > 0) {
            char c = Serial.read();
            if (c == 'q' || c == 'Q') { isPaused = true; Serial.println("STOPPED."); }
            else if (c == 'r' || c == 'R') { isPaused = false; Serial.println("RESUMED."); }
            else if (!isPaused) {
                if (c == 'A' || c == 'B' || c == 'C' || c == 'D') {
                    targetZone = c;
                }
            }
        }
        while (isPaused) {
            if (Serial.available() > 0) {
                char c = Serial.read();
                if (c == 'r' || c == 'R') { isPaused = false; Serial.println("RESUMED."); }
            }
            delay(10);
        }
        delay(10);
    }

    // 3. 구역 인덱스 변환 및 슬롯 계산
    int zoneIdx = -1;
    if      (targetZone == 'A') zoneIdx = 0;
    else if (targetZone == 'B') zoneIdx = 1;
    else if (targetZone == 'C') zoneIdx = 2;
    else if (targetZone == 'D') zoneIdx = 3;

    if (zoneIdx != -1) {
        // 현재 채워야 할 슬롯 번호 (0~3)
        int currentSlot = zoneCounts[zoneIdx]; 

        lcd.setCursor(0,1);
        lcd.print("Zone:"); lcd.print(targetZone);
        lcd.print(" Slot:"); lcd.print(currentSlot + 1);

        Serial.print("Zone: "); Serial.print(targetZone);
        Serial.print(", Slot: "); Serial.println(currentSlot + 1);

        // 4. 로봇 이동 (Pick -> Place)
        pickFromHome();

        if      (zoneIdx == 0) placeToSlot(A_ABOVE, A_TOUCH, A_LIFT, currentSlot);
        else if (zoneIdx == 1) placeToSlot(B_ABOVE, B_TOUCH, B_LIFT, currentSlot);
        else if (zoneIdx == 2) placeToSlot(C_ABOVE, C_TOUCH, C_LIFT, currentSlot);
        else if (zoneIdx == 3) placeToSlot(D_ABOVE, D_TOUCH, D_LIFT, currentSlot);

        // 5. 카운트 증가
        zoneCounts[zoneIdx]++;

        // 6. [중요] 해당 구역이 꽉 찼는지 확인 (4개가 되면)
        if (zoneCounts[zoneIdx] >= 4) {
            lcd.clear();
            lcd.setCursor(0,0);
            lcd.print("Zone "); lcd.print(targetZone); lcd.print(" FULL!");
            lcd.setCursor(0,1);
            lcd.print("Call AGV...");
            Serial.print("ZONE_FULL_"); Serial.println(targetZone); // PC나 AGV가 알 수 있게 출력

            // *** 여기에 나중에 아두이노 카(AGV) 호출 코드를 넣으면 됨 ***
            // 예: Serial1.println("GO_A"); 
            
            safeDelay(3000); // AGV가 와서 가져가는 시간 시뮬레이션 (3초 대기)

            // 팔레트가 비워졌으므로 카운트 리셋
            zoneCounts[zoneIdx] = 0; 
            Serial.println("Zone Reset. Ready.");
        }
    }

    lcd.clear();
    lcd.print("Remove object");
    weightState = 3;
  }

  // -------- 상태 3 : 제거 대기 --------
  else if (weightState == 3) {
    if (fabs(currentWeight) < ZERO_THRESHOLD) {   
      lcd.clear();
      lcd.print("Zeroing...");
      safeDelay(500); 
      scale.tare();
      lcd.clear();
      lcd.print("Ready...");
      lcd.setCursor(0,1);
      lcd.print("Put object");
      weightState = 1;
    }
  }
  safeDelay(100);
}