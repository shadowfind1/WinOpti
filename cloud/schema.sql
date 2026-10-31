CREATE TABLE IF NOT EXISTS runs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  created INTEGER NOT NULL,
  install TEXT NOT NULL,
  game TEXT NOT NULL,
  cpu TEXT NOT NULL,
  gpu TEXT NOT NULL,
  ram INTEGER NOT NULL,
  res TEXT NOT NULL,
  avg REAL NOT NULL,
  low1 REAL NOT NULL,
  version TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS runs_match ON runs (game, gpu, res, cpu);
CREATE INDEX IF NOT EXISTS runs_install ON runs (install, created);
