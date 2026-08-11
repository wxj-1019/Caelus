// Caelus 设计沙盒本地静态服务器：node server.js [port]
// 仅用于沙盒预览与截图矩阵，不要用于生产。
const http = require("http");
const fs = require("fs");
const path = require("path");

const port = Number(process.argv[2]) || 8901;
const root = __dirname;
const MIME = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".md": "text/plain; charset=utf-8"
};

http.createServer((req, res) => {
  let p = path.normalize(decodeURIComponent(req.url.split("?")[0]));
  if (p === path.sep || p.endsWith(path.sep)) p = path.join(p, "index.html");
  const file = path.join(root, p);
  if (!file.startsWith(root)) { res.writeHead(403); res.end("403"); return; }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end("404"); return; }
    res.writeHead(200, { "Content-Type": MIME[path.extname(file)] || "application/octet-stream" });
    res.end(data);
  });
}).listen(port, "127.0.0.1", () => console.log(`sandbox server on http://127.0.0.1:${port}`));
