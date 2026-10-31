// WinOpti: общая база замеров FPS для сравнения «твой ПК против похожих».
// Храним только: игра, модель процессора и видеокарты, объём памяти, разрешение, FPS и случайный id установки
// (чтобы не сравнивать человека с ним самим). IP и что-либо личное не сохраняем.

const MIN_SIMILAR = 5;          // меньше — сравнение не показываем: и шумно, и слишком точно указывает на конкретного человека
const MAX_RUNS_PER_DAY = 30;    // с одной установки за сутки

const text = (s, max) => typeof s === "string" && s.trim().length > 0 && s.length <= max ? s.trim() : null;
const num = (n, min, max) => typeof n === "number" && isFinite(n) && n >= min && n <= max ? n : null;

function json(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json; charset=utf-8" } });
}

function median(values) {
  if (values.length === 0) return null;
  const s = [...values].sort((a, b) => a - b);
  const m = Math.floor(s.length / 2);
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
}

function parseRun(b) {
  const run = {
    install: typeof b.install === "string" && /^[0-9a-f]{32}$/.test(b.install) ? b.install : null,
    game: typeof b.game === "string" && /^[\w .\-]{1,40}$/.test(b.game) ? b.game.toLowerCase() : null,
    cpu: text(b.cpu, 80),
    gpu: text(b.gpu, 80),
    ram: num(b.ram, 1, 1024),
    res: typeof b.res === "string" && /^\d{3,5}x\d{3,5}$/.test(b.res) ? b.res : null,
    avg: num(b.avg, 1, 3000),
    low1: num(b.low1, 0.5, 3000),
    version: text(b.version, 20) ?? "",
  };
  if (Object.entries(run).some(([k, v]) => v === null)) return null;
  if (run.low1 > run.avg * 1.05) return null; // 1% low не может быть выше среднего
  return run;
}

async function addRun(env, run) {
  const day = Math.floor(Date.now() / 86400000) * 86400000;
  const { count } = await env.DB.prepare("SELECT COUNT(*) AS count FROM runs WHERE install = ? AND created >= ?").bind(run.install, day).first();
  if (count >= MAX_RUNS_PER_DAY) return json({ error: "too many runs today" }, 429);
  await env.DB.prepare("INSERT INTO runs (created, install, game, cpu, gpu, ram, res, avg, low1, version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)")
    .bind(Date.now(), run.install, run.game, run.cpu, run.gpu, run.ram, run.res, run.avg, run.low1, run.version).run();
  return json({ ok: true });
}

async function group(env, where, args) {
  const { results } = await env.DB.prepare(`SELECT avg, low1 FROM runs WHERE ${where} ORDER BY created DESC LIMIT 2000`).bind(...args).all();
  if (results.length < MIN_SIMILAR) return { count: results.length };
  return { count: results.length, avg: median(results.map(r => r.avg)), low1: median(results.map(r => r.low1)) };
}

async function compare(env, q) {
  const game = (q.get("game") ?? "").toLowerCase(), cpu = q.get("cpu") ?? "", gpu = q.get("gpu") ?? "", res = q.get("res") ?? "";
  const install = q.get("install") ?? "";
  if (!game || !gpu) return json({ error: "game and gpu required" }, 400);
  // сначала то же железо и разрешение, потом та же видеокарта с любым процессором
  const same = await group(env, "game = ? AND gpu = ? AND cpu = ? AND res = ? AND install != ?", [game, gpu, cpu, res, install]);
  const sameGpu = await group(env, "game = ? AND gpu = ? AND res = ? AND install != ?", [game, gpu, res, install]);
  return json({ same, sameGpu });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    try {
      if (request.method === "POST" && url.pathname === "/v1/runs") {
        let body;
        try { body = await request.json(); } catch { return json({ error: "bad json" }, 400); }
        const run = parseRun(body);
        return run ? addRun(env, run) : json({ error: "bad run" }, 400);
      }
      if (request.method === "GET" && url.pathname === "/v1/compare") return compare(env, url.searchParams);
      return json({ error: "not found" }, 404);
    } catch (e) {
      return json({ error: "server error" }, 500);
    }
  },
};
