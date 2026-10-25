"""Поиск и экспорт/импорт в окне. Диалоги подменены: ничего не применяется. Аргумент: папка для файлов."""
import json
import pathlib
import sys
import traceback

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))  # корень проекта
from PIL import ImageGrab

import gui
import ui.presets_page as pp

OUT = pathlib.Path(sys.argv[1])
export_file = OUT / "export_test.json"
import_file = OUT / "import_test.json"
asked = []
pp.filedialog.asksaveasfilename = lambda **kw: str(export_file)
pp.filedialog.askopenfilename = lambda **kw: str(import_file)
pp.messagebox.askyesno = lambda title, text, parent=None: asked.append(text) or False  # всегда «Нет»

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
    export_file.unlink(missing_ok=True)
    unread = sum(s == "loading" for s in app.states.values())
    app._export_settings()  # сначала дочитывает состояние непрочитанных вкладок, потом сохраняет
    log.append(f"экспорт: непрочитанных твиков было {unread}, файл сразу: {export_file.exists()}")
    wait_export()


def wait_export():
    if app.reading or not export_file.exists():
        app.after(200, wait_export)
        return
    s3()


def s3():
    data = json.loads(export_file.read_text(encoding="utf-8"))
    on_now = sum(1 for s in app.states.values() if s == "on")
    log.append(f"экспорт: твиков в файле {len(data['tweaks'])}, включено сейчас {on_now}")
    off = [tid for tid, s in app.states.items() if s == "off"][:2]
    import_file.write_text(json.dumps({"app": "WinOpti", "format": 1,
                                       "tweaks": data["tweaks"][:3] + off + ["disable_widgets", "no_such_tweak"]}),
                           encoding="utf-8")
    app.admin = True  # чтобы импорт не упёрся в отсутствие прав; askyesno подменён на «Нет»
    app._can_edit = lambda tid: app.states[tid] != "n/a"
    app._import_settings()
    log.append("импорт, окно подтверждения:\n" + (asked[-1] if asked else "(не показано)"))
    import_file.write_text("{\"hello\": 1}", encoding="utf-8")
    app._import_settings()
    log.append("импорт чужого файла: " + app.toast_lbl.cget("text"))
    app.destroy()


app.after(3500, s1)
app.after(60000, app.destroy)  # не виснуть, если в окне ошибка
app.mainloop()
for line in log:
    print(line)
print("ошибок в окне:", len(errors))
for e in errors:
    print(e)
