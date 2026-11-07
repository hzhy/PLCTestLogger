"""极简 Modbus TCP 从站模拟器（功能码 03 读保持寄存器 / 06 写单个寄存器），用于本机联调。

用法: python tools/modbus_sim.py [port] [script]
  script 形如 "3:230=1;6:230=0;9:230=1,200=5,212=3"  表示第 3 秒把 D230 置 1，第 6 秒置 0，……
"""
import socket, struct, sys, threading, time

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 5020
SCRIPT = sys.argv[2] if len(sys.argv) > 2 else "3:230=1;6:230=0;9:200=17,204=11,212=2,230=1;12:230=0"
REGS = [0] * 1000
# 初始值：JC003干湿式, 4H运转后试验, 12接口, 压力20, 200rpm, 温度36.5, 纯水100~200, 结果 0.01..
init = {200: 23, 202: 5, 203: 12, 204: 1, 208: 200, 210: 365, 209: 4}
for i in range(12): init[212 + i] = 1
init[219] = 2
REGS[:] = [0] * 1000
for k, v in init.items(): REGS[k] = v
LOCK = threading.Lock()


def handle(conn):
    try:
        while True:
            hdr = conn.recv(7)
            if len(hdr) < 7: return
            tid, pid, length, uid = struct.unpack(">HHHB", hdr)
            body = conn.recv(length - 1)
            fc = body[0]
            if fc == 3:
                addr, cnt = struct.unpack(">HH", body[1:5])
                with LOCK: vals = REGS[addr:addr + cnt]
                payload = bytes([fc, cnt * 2]) + b"".join(struct.pack(">H", v) for v in vals)
            elif fc == 6:
                addr, val = struct.unpack(">HH", body[1:5])
                with LOCK: REGS[addr] = val
                print(f"[sim] write D{addr}={val}", flush=True)
                payload = body[:5]
            else:
                payload = bytes([fc | 0x80, 1])
            conn.sendall(struct.pack(">HHHB", tid, pid, len(payload) + 1, uid) + payload)
    except (ConnectionResetError, ConnectionAbortedError):
        pass
    finally:
        conn.close()


def scripted():
    t0 = time.time()
    steps = []
    for part in SCRIPT.split(";"):
        at, assigns = part.split(":")
        steps.append((float(at), [tuple(map(int, a.split("="))) for a in assigns.split(",")]))
    for at, assigns in sorted(steps):
        time.sleep(max(0, t0 + at - time.time()))
        with LOCK:
            for addr, val in assigns: REGS[addr] = val
        print(f"[sim] t={at:.0f}s set {assigns}", flush=True)


srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
srv.bind(("127.0.0.1", PORT))
srv.listen(5)
print(f"[sim] listening on 127.0.0.1:{PORT}", flush=True)
threading.Thread(target=scripted, daemon=True).start()
srv.settimeout(1.0)
end = time.time() + 20
while time.time() < end:
    try:
        c, _ = srv.accept()
        print("[sim] client connected", flush=True)
        threading.Thread(target=handle, args=(c,), daemon=True).start()
    except socket.timeout:
        pass
print("[sim] done", flush=True)
