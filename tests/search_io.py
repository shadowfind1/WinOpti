"""Поиск в окне: найденные карточки и их связь с состоянием твиков. Аргумент: папка для скриншота."""
import pathlib
import sys
import traceback

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))  # корень проекта
from PIL import ImageGrab

import gui

OUT = pathlib.Path(sys.argv[1])
errors, log = [], []
app = gui.App()
app.report_callback_exception = lambda *a: errors.append("".join(traceback.format_exception(*a)))


def shot(name):
    app.update()
    x, y = app.winfo_rootx(), app.winfo_rooty()
    ImageGrab.grab(bbox=(x, y, x + app.winfo_width(), y + app.winfo_height())).save(OUT / f"{name}.png")


def type_search(text):
    app.search_entry.delete(0, "end")
    app.search_entry.insert(0, text)
    app._search_run()


def s1():
    for q in ("мыш", "mouse", "телеметр", "zzzнетакого"):
        type_search(q)
        log.append(f"поиск «{q}»: {app.subtitle_lbl.cget('text')} | страница: {app.current} | "
                   f"карточек: {len(app.search_rows)}")
    type_search("мыш")
    app.after(500, s2)


def s2():
    shot("s1_search")
    tid = next(iter(app.search_rows))
    log.append(f"состояние в поиске = в основной карточке: "
               f"{app.search_rows[tid].state == app.states[tid]} ({app.states[tid]})")
    app._on_state(tid, "off", None)  # пришёл ответ фонового потока
    log.append(f"после ответа «off» поиск обновился: {app.search_rows[tid].state}")
    app._on_state(tid, "on", None)
    app._search_clear()
    log.append(f"после очистки поиска вернулись на: {app.current}")
    app.destroy()


app.after(3500, s1)
app.after(60000, app.destroy)  # не виснуть, если в окне ошибка
app.mainloop()
for line in log:
    print(line)
print("ошибок в окне:", len(errors))
for e in errors:
    print(e)
