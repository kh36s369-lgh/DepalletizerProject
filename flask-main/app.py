from flask import Flask
from flask_socketio import SocketIO, emit
import sqlite3
import datetime

app = Flask(__name__)
app.config['SECRET_KEY'] = 'secret!'
# cors_allowed_origins="*" : C# 및 React Native 등 모든 외부 접속 허용
socketio = SocketIO(app, cors_allowed_origins="*") 

# --- DB 초기화 및 테이블 생성 (통합) ---
def init_db():
    # with문을 사용하여 연결 안전성 확보 (자동 close)
    with sqlite3.connect('mbd_logs.db') as conn:
        c = conn.cursor()
        
        # 1. [기존] 시스템 로그 테이블 (C# System Log)
        c.execute('''CREATE TABLE IF NOT EXISTS system_logs 
                     (id INTEGER PRIMARY KEY AUTOINCREMENT, timestamp TEXT, level TEXT, message TEXT)''')
        
        # 2. [공통] 사용자 테이블 (Login)
        c.execute('''CREATE TABLE IF NOT EXISTS users 
                     (id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT UNIQUE, password TEXT)''')
        
        # 3. [기존] 검수 결과 테이블
        c.execute('''CREATE TABLE IF NOT EXISTS inspection_results 
                     (id INTEGER PRIMARY KEY AUTOINCREMENT, 
                      timestamp TEXT, 
                      result TEXT, 
                      zone TEXT, 
                      qr_code TEXT, 
                      image_path TEXT)''') 

        # 4. [신규] 택배 접수 테이블 (App Parcel Data)
        c.execute('''CREATE TABLE IF NOT EXISTS parcels 
                     (id INTEGER PRIMARY KEY AUTOINCREMENT, 
                      tracking_number TEXT UNIQUE, 
                      qr_code TEXT,
                      sender_name TEXT, sender_phone TEXT, sender_address TEXT,
                      receiver_name TEXT, receiver_phone TEXT, receiver_address TEXT,
                      item_name TEXT, item_qty INTEGER, item_weight TEXT,
                      status TEXT, timestamp TEXT)''')
        
        # 5. [신규] AGV 이력 테이블
        c.execute('''CREATE TABLE IF NOT EXISTS agv_history 
                     (id INTEGER PRIMARY KEY AUTOINCREMENT, 
                      timestamp TEXT, 
                      zone TEXT, 
                      action TEXT)''')
        
        # 6. 기본 관리자 계정 생성 (없으면 생성)
        c.execute("SELECT * FROM users WHERE username = 'admin'")
        if not c.fetchone():
            print("[System] 'admin' 계정 생성 (PW: 1234)")
            c.execute("INSERT INTO users (username, password) VALUES (?, ?)", ('admin', '1234'))
        
        # 7. [유지보수] 30일 지난 오래된 시스템 로그 삭제 (DB 용량 관리)
        try:
            c.execute("DELETE FROM system_logs WHERE date(timestamp) < date('now', '-30 days')")
            if c.rowcount > 0:
                print(f"[System] 오래된 로그 정리 완료 (삭제된 행: {c.rowcount})")
        except Exception as e:
            print(f"[System] 로그 정리 중 오류: {e}")

        conn.commit()

# --- Socket.IO 이벤트 핸들러 ---

@socketio.on('connect')
def handle_connect():
    print('[Server] Client connected')
    emit('server_message', {'data': '서버에 연결되었습니다.'})

@socketio.on('disconnect')
def handle_disconnect():
    print('[Server] Client disconnected')

# --- [공통] 로그인 처리 (C# & App 호환) ---
@socketio.on('login_request')
def handle_login(data):
    print(f"[Login] Try: {data}")
    
    # C#은 'Username', App은 'username'을 보낼 수 있으므로 둘 다 체크
    username = data.get('Username') or data.get('username')
    password = data.get('Password') or data.get('password')

    conn = sqlite3.connect('mbd_logs.db')
    c = conn.cursor()
    c.execute("SELECT * FROM users WHERE username = ? AND password = ?", (username, password))
    user = c.fetchone()
    conn.close()

    if user:
        print(f"[Login] Success: {username}")
        emit('login_response', {
            'success': True,           # C#용
            'status': 'success',       # App용
            'message': f'{username}님 환영합니다!',
            'user_name': username
        })
    else:
        print(f"[Login] Failed: {username}")
        emit('login_response', {
            'success': False, 
            'status': 'fail', 
            'message': '아이디 또는 비밀번호가 올바르지 않습니다.'
        })

# --- [로그 기능] 시스템 로그 저장 ---
@socketio.on('send_log')
def handle_log(data):
    # print(f"[Log] {data}") # 로그 너무 많으면 주석 처리
    try:
        with sqlite3.connect('mbd_logs.db') as conn:
            c = conn.cursor()
            c.execute("INSERT INTO system_logs (timestamp, level, message) VALUES (?, ?, ?)",
                      (datetime.datetime.now(), data.get('level'), data.get('message')))
            conn.commit()
    except Exception as e:
        print(f"Log Save Error: {e}")

# --- [AGV 기능] AGV 동작 기록 저장 및 조회 ---
@socketio.on('save_agv_action')
def handle_save_agv_action(data):
    print(f"[AGV Save] {data}")
    try:
        with sqlite3.connect('mbd_logs.db') as conn:
            c = conn.cursor()
            c.execute("INSERT INTO agv_history (timestamp, zone, action) VALUES (?, ?, ?)",
                      (datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S'), data.get('zone'), data.get('action')))
            conn.commit()
    except Exception as e:
        print(f"AGV Save Error: {e}")

@socketio.on('get_agv_history')
def handle_get_agv_history(data=None):
    # print("[AGV History] Request received")
    try:
        conn = sqlite3.connect('mbd_logs.db')
        c = conn.cursor()
        # 최근 50개만 역순으로 가져오기
        c.execute("SELECT timestamp, zone, action FROM agv_history ORDER BY id DESC LIMIT 50")
        rows = c.fetchall()
        conn.close()
        
        results = []
        for r in rows:
            results.append({'timestamp': r[0], 'zone': r[1], 'action': r[2]})
            
        emit('agv_history_response', results)
    except Exception as e:
        print(f"AGV History Error: {e}")

# --- [검수 기능] 검수 결과 저장 ---
@socketio.on('send_inspection')
def handle_inspection(data):
    print(f"[Inspection] {data}")
    
    qr_code = data.get('qr_code')
    result = data.get('result') # 'Normal' 또는 'Defect'
    zone = data.get('zone')
    image_path = data.get('image_path')
    
    conn = sqlite3.connect('mbd_logs.db')
    c = conn.cursor()
    
    try:
        # 1. 검수 이력 저장 (관리자용)
        c.execute("INSERT INTO inspection_results (timestamp, result, zone, qr_code, image_path) VALUES (?, ?, ?, ?, ?)",
                  (datetime.datetime.now(), result, zone, qr_code, image_path))
        
        # 2. 택배 접수 테이블 상태 업데이트 (사용자용)
        new_status = "배송 중" if result == 'Normal' else "반송/불량 대기"
        c.execute("UPDATE parcels SET status = ? WHERE qr_code = ?", (new_status, qr_code))
        
        conn.commit()
        print(f"[DB Update] Parcel status updated: {new_status}")
        
    except Exception as e:
        print(f"[Inspection Error] {e}")
    finally:
        conn.close()

# --- [조회 기능] 검수 기록 조회 ---
@socketio.on('search_history')
def handle_search_history(data):
    print(f"[History Search] {data}")
    query = "SELECT timestamp, qr_code, result, zone, image_path FROM inspection_results WHERE 1=1"
    params = []

    if data.get('date'):
        query += " AND date(timestamp) = date(?)"
        params.append(data['date'])

    if data.get('qr_code'):
        query += " AND qr_code LIKE ?"
        params.append(f"%{data['qr_code']}%")

    if data.get('status') and data.get('status') != '전체':
        status_map = {'정상': 'Normal', '불량': 'Defect'}
        val = status_map.get(data['status'], data['status']) 
        query += " AND result = ?"
        params.append(val)

    query += " ORDER BY id DESC"

    conn = sqlite3.connect('mbd_logs.db')
    c = conn.cursor()
    c.execute(query, params)
    rows = c.fetchall()
    conn.close()

    results = []
    for r in rows:
        results.append({'timestamp': r[0], 'qr_code': r[1], 'result': r[2], 'zone': r[3], 'image_path': r[4]})
    
    emit('search_history_response', results)

# --- [조회 기능] 택배 배송 조회 ---
@socketio.on('search_delivery')
def handle_search_delivery(data):
    print(f"[Delivery Search] {data}")
    
    query = "SELECT timestamp, qr_code, status, item_name, sender_name, receiver_address, receiver_name FROM parcels WHERE 1=1"
    params = []
    
    if data.get('date'):
        query += " AND date(timestamp) = date(?)"
        params.append(data['date'])
        
    if data.get('tracking_number'):
        query += " AND (tracking_number LIKE ? OR qr_code LIKE ?)"
        params.append(f"%{data['tracking_number']}%")
        params.append(f"%{data['tracking_number']}%")
        
    query += " ORDER BY id DESC"
    
    conn = sqlite3.connect('mbd_logs.db')
    c = conn.cursor()
    c.execute(query, params)
    rows = c.fetchall()
    conn.close()
    
    results = []
    for r in rows:
        results.append({
            'timestamp': r[0], 
            'qr_code': r[1], 
            'result': r[2],      
            'item_name': r[3],   
            'sender_name': r[4], 
            'address': r[5],
            'receiver_name': r[6]
        })
        
    print(f"[Delivery Search] Found {len(results)} items.")
    emit('search_delivery_response', results)

# --- [통계 기능] 대시보드 통계 ---
@socketio.on('get_dashboard_stats')
def handle_dashboard_stats(data=None):
    today = datetime.datetime.now().strftime('%Y-%m-%d')
    conn = sqlite3.connect('mbd_logs.db')
    conn.row_factory = sqlite3.Row 
    c = conn.cursor()
    
    # 1. 총 물량
    c.execute("SELECT COUNT(*) as count FROM parcels WHERE date(timestamp) = ?", (today,))
    result_reg = c.fetchone()
    registered_count = result_reg['count'] if result_reg else 0

    # 2. 검수 결과
    c.execute("SELECT COUNT(*) as total_inspected, SUM(CASE WHEN result = 'Normal' THEN 1 ELSE 0 END) as normal, SUM(CASE WHEN result = 'Defect' THEN 1 ELSE 0 END) as defect FROM inspection_results WHERE date(timestamp) = ?", (today,))
    row = c.fetchone()
    
    summary = {
        'registered': registered_count,
        'inspected': row['total_inspected'] if row and row['total_inspected'] else 0,
        'normal': row['normal'] if row and row['normal'] else 0, 
        'defect': row['defect'] if row and row['defect'] else 0
    }
    
    # 3. 구역별 통계
    c.execute("SELECT zone, COUNT(*) as count FROM inspection_results WHERE date(timestamp) = ? GROUP BY zone", (today,))
    zones = {r['zone']: r['count'] for r in c.fetchall()} 
    
    # 4. ★★★ [수정됨] 최근 시스템 로그 (성공/실패 모두 포함, 최근 7개) ★★★
    # 기존에 있던 "AND level IN ('Warning', 'Error')" 조건을 삭제했습니다.
    c.execute("SELECT timestamp, level, message FROM system_logs WHERE date(timestamp) = ? ORDER BY id DESC LIMIT 7", (today,))
    alarms = [{'timestamp': r['timestamp'], 'level': r['level'], 'message': r['message']} for r in c.fetchall()]

    conn.close()
    emit('dashboard_stats_response', {'summary': summary, 'zones': zones, 'alarms': alarms})

# --- [모바일 앱 기능] 택배 접수 ---
@socketio.on('register_parcel')
def handle_register_parcel(data):
    print(f"[App Register] {data}")
    qr_code_val = data.get('qr_code')
    tracking_num = qr_code_val 
    
    try:
        conn = sqlite3.connect('mbd_logs.db')
        c = conn.cursor()
        c.execute("""
            INSERT INTO parcels (
                tracking_number, qr_code, 
                sender_name, sender_phone, sender_address,
                receiver_name, receiver_phone, receiver_address, 
                item_name, item_qty, item_weight,
                status, timestamp
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            tracking_num, qr_code_val,
            data['sender']['name'], data['sender']['phone'], data['sender']['address'],
            data['receiver']['name'], data['receiver']['phone'], data['receiver']['address'],
            data['item']['name'], data['item'].get('quantity', 1), data['item'].get('weight', ''),
            '접수 완료', datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')
        ))
        conn.commit()
        conn.close()
        emit('register_response', {'status': 'success', 'message': '접수 완료', 'tracking_number': tracking_num})
    except sqlite3.IntegrityError:
        emit('register_response', {'status': 'fail', 'message': '이미 등록된 운송장(QR)입니다.'})
    except Exception as e:
        print(f"Error: {e}")
        emit('register_response', {'status': 'fail', 'message': '서버 오류 발생'})

@socketio.on('search_parcel')
def handle_search_parcel(data):
    keyword = data.get('keyword')
    print(f"[App Search] Keyword: {keyword}")

    conn = sqlite3.connect('mbd_logs.db')
    conn.row_factory = sqlite3.Row
    c = conn.cursor()
    c.execute("SELECT * FROM parcels WHERE qr_code = ? OR tracking_number = ?", (keyword, keyword))
    row = c.fetchone()
    conn.close()

    if row:
        result_data = {
            'tracking_number': row['tracking_number'],
            'status': row['status'],
            'sender_name': row['sender_name'],
            'receiver_name': row['receiver_name'],
            'item_name': row['item_name'],
            'timestamp': row['timestamp']
        }
        emit('search_response', {'status': 'success', 'data': result_data})
    else:
        emit('search_response', {'status': 'fail', 'message': '조회된 내역이 없습니다.'})

@socketio.on('find_by_tracking_number')
def handle_find_by_tracking(data):
    handle_search_parcel({'keyword': data.get('tracking_number')})


if __name__ == '__main__':
    init_db()
    print("Flask SocketIO Server Running on port 5000...")
    socketio.run(app, host='0.0.0.0', port=5000, debug=True)