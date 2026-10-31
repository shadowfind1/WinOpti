// Локальная проверка worker.js без Cloudflare: D1 подменяем встроенным SQLite из Node 24.
// Запуск: node cloud/test/local_server.mjs [порт]   (база в памяти, с тестовыми замерами похожих ПК)
import { DatabaseSync } from "node:sqlite";
import { createServer } from "node:http";
import { readFileSync } from "node:fs";
import worker from "../src/worker.js";

const db = new DatabaseSync(":memory:");
db.exec(readFileSync(new URL("../schema.sql", import.meta.url), "utf8"));

// минимальная обёртка с интерфейсом D1: prepare().bind().first()/all()/run()
const D1 = {
  prepare(sql) {
    let args = [];
    const stmt = db.prepare(sql);
    return {
      bind(...a) { args = a; return this; },
      async first() { return stmt.get(...args) ?? null; },
      async all() { return { results: stmt.all(...args) }; },
      async run() { stmt.run(...args); return { success: true }; },
    };
  },
};

// тестовые «чужие» замеры: 6 похожих ПК в CS2
const seed = db.prepare("INSERT INTO runs (created, install, game, cpu, gpu, ram, res, avg, low1, version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
for (const [avg, low] of [[305, 128], [318, 140], [296, 121], [311, 133], [322, 145], [309, 130]])
  seed.run(Date.now(), "f".repeat(31) + Math.floor(Math.random() * 10), "cs2", process.env.CPU ?? "Ryzen 5 3600", process.env.GPU ?? "GeForce GTX 1070 Ti", 16, process.env.RES ?? "1920x1080", avg, low, "test");

const port = Number(process.argv[2] ?? 8787);
createServer(async (req, res) => {
  const chunks = [];
  for await (const c of req) chunks.push(c);
  const body = chunks.length ? Buffer.concat(chunks) : undefined;
  const request = new Request(`http://127.0.0.1:${port}${req.url}`, { method: req.method, headers: req.headers, body: req.method === "GET" ? undefined : body });
  const response = await worker.fetch(request, { DB: D1 });
  const text = await response.text();
  console.log(req.method, req.url, response.status, text);
  res.writeHead(response.status, { "content-type": "application/json" });
  res.end(text);
}).listen(port, "127.0.0.1", () => console.log("listening on " + port));
