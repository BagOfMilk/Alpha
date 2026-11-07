#!/usr/bin/env python3
# Сторінка анімацій «Рингу». Запуск однією командою, без інтернету:
#   python tools/ring_preview/run.py
# Піднімає локальний сервер і відкриває браузер. Ключі: --port 8765, --no-browser.
# Службове для перевірки очима: змінна RING_SHOTS=<тека> дозволяє сторінці зберігати кадри (POST /__shot?name=x).
import http.server, os, socketserver, sys, threading, webbrowser

HERE = os.path.dirname(os.path.abspath(__file__))
PORT = int(sys.argv[sys.argv.index("--port") + 1]) if "--port" in sys.argv else 8765


class H(http.server.SimpleHTTPRequestHandler):
    extensions_map = dict(http.server.SimpleHTTPRequestHandler.extensions_map, **{".js": "text/javascript", ".mjs": "text/javascript", ".glb": "model/gltf-binary", ".webp": "image/webp", ".json": "application/json"})

    def __init__(self, *a, **k):
        super().__init__(*a, directory=HERE, **k)

    def end_headers(self):
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()

    def log_message(self, *a):
        pass

    def do_POST(self):
        import urllib.parse
        u = urllib.parse.urlparse(self.path)
        dst = os.environ.get("RING_SHOTS")
        if u.path != "/__shot" or not dst:
            self.send_error(404)
            return
        name = urllib.parse.parse_qs(u.query).get("name", ["shot"])[0]
        name = "".join(c for c in name if c.isalnum() or c in "-_.")
        data = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        os.makedirs(dst, exist_ok=True)
        with open(os.path.join(dst, name + ".jpg"), "wb") as f:
            f.write(data)
        self.send_response(200)
        self.end_headers()


class S(socketserver.ThreadingTCPServer):
    # На Windows цей прапорець дає двом серверам один порт, і відповідає випадковий із них.
    allow_reuse_address = os.name != "nt"
    daemon_threads = True


def bind(first):
    """Порт зайнятий (наприклад, сторінка вже запущена) — беремо наступний вільний."""
    for port in range(first, first + 20):
        try:
            return S(("127.0.0.1", port), H), port
        except OSError:
            continue
    sys.exit("Немає вільного порту в межах %d-%d" % (first, first + 19))


sys.stdout.reconfigure(encoding="utf-8")
server, PORT = bind(PORT)
with server as srv:
    url = "http://127.0.0.1:%d/" % PORT
    print("Ринг: сторінка анімацій —", url, flush=True)
    if "--no-browser" not in sys.argv:
        threading.Timer(0.4, lambda: webbrowser.open(url)).start()
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass
