#include <Wire.h>
#include <Adafruit_PWMServoDriver.h>
#include <WiFi.h>
#include <WebServer.h>
#include <ESP32Servo.h>

#include "soc/soc.h"
#include "soc/rtc_cntl_reg.h"

// ===================== 집 공유기 설정 =====================
const char* ssid = "tplink1002";       
const char* password = "dptnsl12";     

IPAddress local_IP(192, 168, 1, 106);
IPAddress gateway(192, 168, 1, 1);     
IPAddress subnet(255, 255, 255, 0);
IPAddress primaryDNS(8, 8, 8, 8);      
// =======================================================

WebServer server(80);
Adafruit_PWMServoDriver pwm = Adafruit_PWMServoDriver();
Servo forkliftServo;

#define ENA_CHANNEL 0 
#define ENB_CHANNEL 3 

#define IN1 13
#define IN2 26 
#define IN3 14
#define IN4 27

#define TRIG_PIN_L 5
#define ECHO_PIN_L 18
#define TRIG_PIN_R 19
#define ECHO_PIN_R 23

#define FORKLIFT_PIN 4 
#define FORKLIFT_UP_ANGLE   70    
#define FORKLIFT_DOWN_ANGLE 10    

#define ANGLE_STEP 53       
#define LIFT_OFFSET 10      
#define ANGLE_TRANSPORT 63  
#define ANGLE_FLOOR 15      

int currentForkliftAngle = FORKLIFT_DOWN_ANGLE; 

#define PIN_S1 34 
#define PIN_S2 35 
#define PIN_S3 32 
#define PIN_S5 33 

// ==================== [속도 상향 조정] ====================
// 짐을 실었을 때를 대비해 전체적으로 힘을 키웠습니다.
const int SPEED_FWD  = 3800;      // (기존 3300 -> 3800) 직진 힘 강화
const int SPEED_LINE_MAX = 3500;  // (기존 3200 -> 3500) 라인트레이싱 주행 힘 강화
const int SPEED_TURN_SOFT = 4000; // (기존 3600 -> 4000) 커브 돌 때 힘 부족 해결!

const int SPEED_PIVOT_HARD = 4095; // 최대 속도 유지
const int SPEED_SCAN = 3000;       // (기존 2800 -> 3000) 탐색도 조금 더 힘차게
// ====================================================

const float STOP_DISTANCE = 8.5; 

const int BLACK = 0;
const int WHITE = 1;

int operatingMode = 0;
int lastLineDirection = 0; 

int targetZone = 0;   
int lineCount = 0;    
int jobPhase = 0;     
const int WAREHOUSE_LINE = 4; 

unsigned long t_s1 = 0; 
unsigned long t_s5 = 0;

unsigned long lastStationTime = 0;

void stopMotors();
void goForward(int speed);
void goBack(int speed);
void turnLeft(int speed);
void turnRight(int speed);
void setForklift(int targetAngle); 
void runLineFollower();
void sendHTMLPage();
void rotateLeftUntilLine();  
void rotateRightUntilLine(); 
void turnAround();          
void goForwardUntilLine();  
void goForwardUntilLineEnd();
float getDistance(int trigPin, int echoPin); 
void goForwardUntilPallet(); 
bool checkStop();
void smartDelay(unsigned long ms);

bool checkStop() {
  server.handleClient(); 
  if (operatingMode == 0) { 
    stopMotors(); 
    return true;  
  }
  return false;
}

void smartDelay(unsigned long ms) {
  unsigned long start = millis();
  while (millis() - start < ms) {
    if (checkStop()) break; 
  }
}

void setup() {
  pinMode(FORKLIFT_PIN, OUTPUT);
  digitalWrite(FORKLIFT_PIN, LOW);

  pinMode(TRIG_PIN_L, OUTPUT); pinMode(ECHO_PIN_L, INPUT);
  pinMode(TRIG_PIN_R, OUTPUT); pinMode(ECHO_PIN_R, INPUT);

  WRITE_PERI_REG(RTC_CNTL_BROWN_OUT_REG, 0);

  Serial.begin(115200);
  Wire.begin(21, 22);
  
  pwm.begin();
  pwm.setPWMFreq(330); 
  
  int initialPulse = map(ANGLE_FLOOR, 0, 180, 500, 2400);
  forkliftServo.writeMicroseconds(initialPulse);
  forkliftServo.attach(FORKLIFT_PIN, 500, 2400);
  currentForkliftAngle = ANGLE_FLOOR;

  pinMode(IN1, OUTPUT); pinMode(IN2, OUTPUT); 
  pinMode(IN3, OUTPUT); pinMode(IN4, OUTPUT);
  pinMode(PIN_S1, INPUT); pinMode(PIN_S2, INPUT);
  pinMode(PIN_S3, INPUT); pinMode(PIN_S5, INPUT);

  analogReadResolution(10);
  stopMotors();

  Serial.println("WiFi Connecting...");
  if (!WiFi.config(local_IP, gateway, subnet, primaryDNS)) {
    Serial.println("STA Failed to configure");
  }
  WiFi.setSleep(false); 
  WiFi.begin(ssid, password);
  while (WiFi.status() != WL_CONNECTED) { delay(500); Serial.print("."); }
  Serial.println("\nWiFi Connected!");
  Serial.print("AGV IP Address: ");
  Serial.println(WiFi.localIP()); 

  server.on("/", []() { sendHTMLPage(); });
  server.on("/taskA", []() { operatingMode = 5; targetZone = 1; jobPhase = 1; lineCount = 0; lastStationTime = 0; Serial.println("CMD: Task A"); server.send(200, "text/plain", "OK"); });
  server.on("/taskB", []() { operatingMode = 5; targetZone = 2; jobPhase = 1; lineCount = 0; lastStationTime = 0; Serial.println("CMD: Task B"); server.send(200, "text/plain", "OK"); });
  server.on("/taskC", []() { operatingMode = 5; targetZone = 3; jobPhase = 1; lineCount = 0; lastStationTime = 0; Serial.println("CMD: Task C"); server.send(200, "text/plain", "OK"); });
  server.on("/forward",   []() { operatingMode = 1; server.send(200, "text/plain", "OK"); });
  server.on("/backward",  []() { operatingMode = 2; server.send(200, "text/plain", "OK"); });
  server.on("/left",      []() { operatingMode = 3; server.send(200, "text/plain", "OK"); });
  server.on("/right",     []() { operatingMode = 4; server.send(200, "text/plain", "OK"); });
  server.on("/stop",      []() { operatingMode = 0; server.send(200, "text/plain", "OK"); });
  
  server.on("/liftup", []() { 
      int prev = operatingMode;
      if(prev == 0) operatingMode = 99; 
      setForklift(ANGLE_TRANSPORT); 
      operatingMode = prev; 
      server.send(200, "text/plain", "OK"); 
  }); 
  server.on("/liftstep", []() { 
      int prev = operatingMode;
      if(prev == 0) operatingMode = 99;
      setForklift(ANGLE_STEP); 
      operatingMode = prev;
      server.send(200, "text/plain", "OK"); 
  });      
  server.on("/liftdown", []() { 
      int prev = operatingMode;
      if(prev == 0) operatingMode = 99;
      setForklift(ANGLE_FLOOR); 
      operatingMode = prev;
      server.send(200, "text/plain", "OK"); 
  });     

  server.begin();
}

void loop() {
  if (checkStop()) return;

  if (operatingMode == 5) runLineFollower();
  else {
    switch (operatingMode) {
      case 0: stopMotors(); break;
      case 1: goForward(SPEED_FWD); break;
      case 2: goBack(SPEED_FWD); break;
      case 3: turnLeft(SPEED_TURN_SOFT); break;
      case 4: turnRight(SPEED_TURN_SOFT); break;
    }
  }
}

float getDistance(int trigPin, int echoPin) {
  digitalWrite(trigPin, LOW);
  delayMicroseconds(2);
  digitalWrite(trigPin, HIGH);
  delayMicroseconds(10);
  digitalWrite(trigPin, LOW);
  long duration = pulseIn(echoPin, HIGH, 30000); 
  if (duration == 0) return 100.0; 
  return duration * 0.034 / 2;
}

void goForwardUntilPallet() {
  Serial.println("동작: 팔레트 감지 전진...");
  goForward(SPEED_FWD);
  unsigned long startTime = millis();
  while (true) {
    if (checkStop()) return;

    float distL = getDistance(TRIG_PIN_L, ECHO_PIN_L);
    float distR = getDistance(TRIG_PIN_R, ECHO_PIN_R);
    if ((distL > 0 && distL <= STOP_DISTANCE) || (distR > 0 && distR <= STOP_DISTANCE)) {
      stopMotors(); Serial.println(">> 도착"); break;
    }
    if (millis() - startTime > 3000) { stopMotors(); break; }
    smartDelay(50); 
  }
}

void turnAround() {
  Serial.println("동작: 180도 피벗 회전 중...");
  stopMotors(); smartDelay(200);
  if (checkStop()) return;
  
  turnLeft(SPEED_PIVOT_HARD);
  smartDelay(800); 
  turnLeft(SPEED_SCAN); 

  if (checkStop()) return;
  unsigned long startTime = millis();
  while (true) {
    if (checkStop()) return;
    int s3_val = analogRead(PIN_S3);
    if (s3_val < 700) { stopMotors(); break; } 
    if (millis() - startTime > 5000) { stopMotors(); break; } 
  }
  smartDelay(500);
}

void goForwardUntilLine() {
  Serial.println("동작: 출구 전진 중...");
  goForward(SPEED_FWD);
  unsigned long startTime = millis();
  while (true) {
    if (checkStop()) return;
    int s1 = digitalRead(PIN_S1);
    int s5 = digitalRead(PIN_S5);
    int s3 = analogRead(PIN_S3);
    if (s1 == BLACK || s5 == BLACK || s3 < 700) { stopMotors(); break; }
    if (millis() - startTime > 5000) { stopMotors(); break; }
  }
  smartDelay(500);
}

void goForwardUntilLineEnd() {
  Serial.println("동작: 라인 끝까지 전진...");
  goForward(SPEED_FWD);
  unsigned long startTime = millis();
  while (true) {
    if (checkStop()) return;
    int s1 = digitalRead(PIN_S1);
    int s2 = digitalRead(PIN_S2);
    int s5 = digitalRead(PIN_S5);
    int s3_val = analogRead(PIN_S3);
    int s3 = (s3_val < 700) ? BLACK : WHITE; 

    if (s1 == WHITE && s2 == WHITE && s3 == WHITE && s5 == WHITE) {
      stopMotors(); 
      Serial.println(">> 라인 끝 감지! 정지.");
      break; 
    }
    if (millis() - startTime > 5000) { stopMotors(); break; }
  }
  smartDelay(500);
}

void setForklift(int targetAngle) {
  if (targetAngle > FORKLIFT_UP_ANGLE) targetAngle = FORKLIFT_UP_ANGLE;
  if (targetAngle < FORKLIFT_DOWN_ANGLE) targetAngle = FORKLIFT_DOWN_ANGLE;
  if (!forkliftServo.attached()) forkliftServo.attach(FORKLIFT_PIN, 500, 2400);
  
  if (currentForkliftAngle < targetAngle) {
    for (int i = currentForkliftAngle; i <= targetAngle; i++) { 
        if (checkStop()) return; 
        forkliftServo.write(i); 
        delay(8); 
        checkStop();
    }
  } else {
    for (int i = currentForkliftAngle; i >= targetAngle; i--) { 
        if (checkStop()) return;
        forkliftServo.write(i); 
        delay(8); 
        checkStop();
    }
  }
  forkliftServo.write(targetAngle); 
  currentForkliftAngle = targetAngle;
}

void rotateLeftUntilLine() {
  Serial.println(">> 좌회전 (피벗)...");
  stopMotors(); smartDelay(500); 
  if (checkStop()) return;
  
  goForward(SPEED_FWD);
  smartDelay(400); 
  stopMotors(); smartDelay(100);
  if (checkStop()) return;

  turnLeft(SPEED_PIVOT_HARD); 
  smartDelay(600); 
  if (checkStop()) return;
  
  turnLeft(SPEED_SCAN); 

  unsigned long startTime = millis();
  while (true) {
    if (checkStop()) return;
    int s3_val = analogRead(PIN_S3);
    if (s3_val < 700) { stopMotors(); break; }
    if (millis() - startTime > 5000) { stopMotors(); break; }
  }
  smartDelay(500);
}

void rotateRightUntilLine() {
  Serial.println(">> 우회전 (피벗)...");
  stopMotors(); smartDelay(500);
  if (checkStop()) return;

  goForward(SPEED_FWD);
  smartDelay(400);
  stopMotors(); smartDelay(100);
  if (checkStop()) return;
  
  turnRight(SPEED_PIVOT_HARD); 
  smartDelay(600); 
  if (checkStop()) return;
  
  turnRight(SPEED_SCAN);

  unsigned long startTime = millis();
  while (true) {
    if (checkStop()) return;
    int s3_val = analogRead(PIN_S3);
    if (s3_val < 700) { stopMotors(); break; }
    if (millis() - startTime > 5000) { stopMotors(); break; }
  }
  smartDelay(500);
}

void runLineFollower() {
  if (checkStop()) return; 

  int s1 = digitalRead(PIN_S1);
  int s2 = digitalRead(PIN_S2);
  int s5 = digitalRead(PIN_S5);
  int s3_val = analogRead(PIN_S3);
  int s3 = (s3_val < 700) ? BLACK : WHITE;

  if (s1 == BLACK) t_s1 = millis();
  if (s5 == BLACK) t_s5 = millis();
  bool crossDetected = false;
  unsigned long now = millis();

  if ( (s1 == BLACK && s5 == BLACK) || 
       ((now - t_s1 < 500) && (now - t_s5 < 500)) ||
       (s1 == BLACK && s2 == BLACK && s3 == BLACK) || 
       (s5 == BLACK && s2 == BLACK && s3 == BLACK) ) { 
    crossDetected = true; 
    t_s1 = 0; t_s5 = 0; 
  }

  if (crossDetected) {
    if (millis() - lastStationTime < 2000) {
        crossDetected = false;
    } else {
        lastStationTime = millis(); 
        
        lineCount++; 
        Serial.print("Station: "); Serial.println(lineCount);
        Serial.print("Target: "); Serial.println(targetZone);

        if (lineCount > WAREHOUSE_LINE) { lineCount = 1; }

        bool isTarget = false;
        if (jobPhase == 1 && lineCount == targetZone) isTarget = true;
        if (jobPhase == 2 && lineCount == WAREHOUSE_LINE) isTarget = true;
        if (jobPhase == 3 && lineCount == targetZone) isTarget = true;

        if (isTarget) {
            stopMotors(); 
            Serial.println(">> 목표 도착! 작업 시작.");
            smartDelay(1000); 
            
            if (jobPhase == 1) { 
                rotateLeftUntilLine(); if (checkStop()) return; 
                setForklift(ANGLE_STEP); smartDelay(500); if (checkStop()) return;
                goForwardUntilPallet(); if (checkStop()) return;
                setForklift(ANGLE_STEP + LIFT_OFFSET); smartDelay(500); if (checkStop()) return;
                
                Serial.println("후진 및 회전...");
                goBack(SPEED_FWD); smartDelay(600); stopMotors(); smartDelay(200); if (checkStop()) return;
                turnAround(); if (checkStop()) return;
                setForklift(ANGLE_TRANSPORT); 
                goForwardUntilLine(); if (checkStop()) return;
                rotateLeftUntilLine(); if (checkStop()) return;
                jobPhase = 2; 
            }
            else if (jobPhase == 2) { 
                rotateLeftUntilLine(); if (checkStop()) return;
                goForwardUntilLineEnd(); if (checkStop()) return;
                setForklift(ANGLE_FLOOR);      
                goBack(SPEED_FWD); smartDelay(800); stopMotors(); 
                smartDelay(5000); if (checkStop()) return;
                setForklift(ANGLE_FLOOR);      
                goForwardUntilLineEnd(); if (checkStop()) return;
                setForklift(ANGLE_FLOOR + LIFT_OFFSET); 
                goBack(SPEED_FWD); smartDelay(600); stopMotors(); smartDelay(200); if (checkStop()) return;
                turnAround(); if (checkStop()) return;
                setForklift(ANGLE_TRANSPORT); 
                goForwardUntilLine(); if (checkStop()) return;
                rotateLeftUntilLine(); if (checkStop()) return;
                jobPhase = 3; 
            }
            else if (jobPhase == 3) { 
                rotateLeftUntilLine(); if (checkStop()) return;
                setForklift(ANGLE_STEP + LIFT_OFFSET);
                goForwardUntilPallet(); if (checkStop()) return;
                setForklift(ANGLE_STEP); 
                goBack(SPEED_FWD); smartDelay(600); stopMotors(); smartDelay(200); if (checkStop()) return;
                turnAround(); if (checkStop()) return;
                goForwardUntilLine(); if (checkStop()) return;
                rotateLeftUntilLine(); if (checkStop()) return;
                stopMotors(); operatingMode = 0; jobPhase = 0; targetZone = 0; return; 
            }
        } else {
            Serial.println(">> 통과 (Pass)");
            goForward(SPEED_FWD); 
            smartDelay(600); 
            return; 
        }
    }
  }

  if (s1 == BLACK) { turnLeft(SPEED_PIVOT_HARD); lastLineDirection = 1; }
  else if (s5 == BLACK) { turnRight(SPEED_PIVOT_HARD); lastLineDirection = 2; }
  else if (s2 == BLACK && s3 == WHITE) { turnLeft(SPEED_TURN_SOFT); lastLineDirection = 1; }
  else if (s3 == BLACK && s2 == WHITE) { turnRight(SPEED_TURN_SOFT); lastLineDirection = 2; }
  else if (s2 == BLACK || s3 == BLACK) { goForward(SPEED_LINE_MAX); lastLineDirection = 0; }
  else { 
    if (lastLineDirection == 1) turnLeft(SPEED_PIVOT_HARD); 
    else if (lastLineDirection == 2) turnRight(SPEED_PIVOT_HARD); 
    else stopMotors(); 
  }
}

void stopMotors() {
  digitalWrite(IN1, LOW); digitalWrite(IN2, LOW); digitalWrite(IN3, LOW); digitalWrite(IN4, LOW);
  pwm.setPWM(ENA_CHANNEL, 0, 0); pwm.setPWM(ENB_CHANNEL, 0, 0);
}
void goForward(int speed) {
  digitalWrite(IN1, HIGH); digitalWrite(IN2, LOW); digitalWrite(IN3, HIGH); digitalWrite(IN4, LOW);
  pwm.setPWM(ENA_CHANNEL, 0, speed); pwm.setPWM(ENB_CHANNEL, 0, speed);
}
void goBack(int speed) {
  digitalWrite(IN1, LOW); digitalWrite(IN2, HIGH); digitalWrite(IN3, LOW); digitalWrite(IN4, HIGH);
  pwm.setPWM(ENA_CHANNEL, 0, speed); pwm.setPWM(ENB_CHANNEL, 0, speed);
}
void turnLeft(int speed) {
  digitalWrite(IN1, LOW); digitalWrite(IN2, LOW); 
  pwm.setPWM(ENA_CHANNEL, 0, 0);
  digitalWrite(IN3, HIGH); digitalWrite(IN4, LOW); 
  pwm.setPWM(ENB_CHANNEL, 0, speed);
}
void turnRight(int speed) {
  digitalWrite(IN1, HIGH); digitalWrite(IN2, LOW); 
  pwm.setPWM(ENA_CHANNEL, 0, speed);
  digitalWrite(IN3, LOW); digitalWrite(IN4, LOW); 
  pwm.setPWM(ENB_CHANNEL, 0, 0);
}

void sendHTMLPage() {
    String html = "<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>";
    html += "<style>body{font-family:sans-serif;text-align:center;padding:10px;background:#eee;}";
    html += ".btn{padding:15px;margin:5px;font-size:18px;width:90%;max-width:300px;border-radius:10px;border:none;color:white;font-weight:bold; touch-action:manipulation; user-select:none;}";
    html += ".nav{background:#555;} .taskA{background:#FF5733;} .taskB{background:#33FF57;color:#000;} .taskC{background:#3357FF;} .stop{background:#C70039;}";
    html += ".lift{background:#F39C12; color:black;} h2{color:#333; margin-bottom:10px;}</style>";
    html += "<script>function s(u){fetch(u).catch(()=>{});} function p(c){s('/'+c);}</script>";
    html += "</head><body>";
    html += "<h2>Smart Factory Control</h2>";
    html += "<button class='btn taskA' onclick=\"p('taskA')\">TASK A (1번 구역)</button><br>";
    html += "<button class='btn taskB' onclick=\"p('taskB')\">TASK B (2번 구역)</button><br>";
    html += "<button class='btn taskC' onclick=\"p('taskC')\">TASK C (3번 구역)</button><br><br>";
    html += "<button class='btn nav' onmousedown=\"p('forward')\" ontouchstart=\"p('forward')\" onmouseup=\"p('stop')\" ontouchend=\"p('stop')\">FORWARD</button><br>";
    html += "<div style='display:flex;justify-content:center;'>";
    html += "<button class='btn nav' style='width:45%' onmousedown=\"p('left')\" ontouchstart=\"p('left')\" onmouseup=\"p('stop')\" ontouchend=\"p('stop')\">LEFT</button>";
    html += "<button class='btn nav' style='width:45%' onmousedown=\"p('right')\" ontouchstart=\"p('right')\" onmouseup=\"p('stop')\" ontouchend=\"p('stop')\">RIGHT</button></div>";
    html += "<button class='btn nav' onmousedown=\"p('backward')\" ontouchstart=\"p('backward')\" onmouseup=\"p('stop')\" ontouchend=\"p('stop')\">BACKWARD</button><br>";
    html += "<div style='display:flex;justify-content:center; margin-top:10px; flex-wrap:wrap;'>";
    html += "<button class='btn lift' style='width:45%' onmousedown=\"p('liftup')\" ontouchstart=\"p('liftup')\">LIFT UP (63)</button>";
    html += "<button class='btn lift' style='width:45%' onmousedown=\"p('liftstep')\" ontouchstart=\"p('liftstep')\">LIFT STEP (53)</button>";
    html += "<button class='btn lift' style='width:90%; margin-top:5px;' onmousedown=\"p('liftdown')\" ontouchstart=\"p('liftdown')\">LIFT DOWN (15)</button></div><br>";
    html += "<button class='btn stop' onclick=\"p('stop')\">STOP ALL</button>";
    html += "</body></html>";
    server.send(200, "text/html", html);
}