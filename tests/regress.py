"""Регрессионный прогон WinOpti в окне (без прав администратора):
все вкладки, мастер удаления на фиктивной программе, скриншоты. Аргумент: папка для скриншотов."""
import os
import pathlib
import sys
import traceback

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))  # корень проекта
from PIL import ImageGrab

import apps
import engine
import gui

OUT = pathlib.Path(sys.argv[1])
NAME = "WinOptiDummyApp"
U = apps.UNINSTALL + "\\" + NAME
local = pathlib.Path(os.environ["LOCALAPPDATA"]) / NAME
local.mkdir(exist_ok=True)
(local / "a.bin").write_bytes(b"x" * 3000)
engine.reg_write("HKCU", f"Software\\{NAME}", "Setting", "DWORD", 7)
engine.reg_write("HKCU", U, "DisplayName", "SZ", NAME)
engine.reg_write("HKCU", U, "UninstallString", "SZ", f'reg.exe delete "HKCU\\{U}" /f')

errors, log = [], []
app = gui.App()
app.report_callback_exception = lambda *a: errors.append("".join(traceback.format_exception(*a)))


def shot(name):
    app.update()
    x, y = app.winfo_rootx(), app.winfo_rooty()
    ImageGrab.grab(bbox=(x, y, x + app.winfo_width(), y + app.winfo_height())).save(OUT / f"{name}.png")


steps = list(app.sections)


def walk():
    if steps:
        app._show(steps.pop(0))
        app.after(600, walk)
        return
    hidden = [c for c, it in app.nav.items() if it["wrap"].winfo_height() <= 1]
    log.append(f"пункты меню без места: {hidden or 'нет'}")
    app._show("presets")
    app.after(600, lambda: (shot("r1_presets"), app._show("gaming"), app.after(900, s2)))


def s2():
    app._status(gui.tr("{name}: готово.").format(name="Test"))
    app.after(700, lambda: (shot("r2_gaming"), app._show("apps"), app.after(2500, s3)))


def s3():
    shot("r3_apps")
    prog = next(p for p in app.apps_data["programs"] if p["name"] == NAME)
    global dlg
    dlg = gui.UninstallDialog(app, prog)
    app.after(800, dlg._start)
    app.after(6000, s4)


def s4():
    log.append(f"мастер: остатков {len(dlg.vars)}, отмечено {sum(v.get() for v, _ in dlg.vars)}")
    dlg._remove()
    app.after(3000, s5)


def s5():
    log.append("мастер, итог: " + dlg.text.cget("text").split("\n")[0][:90])
    dlg._close()
    app._show("cleanup")
    app.after(3000, lambda: (shot("r4_cleanup"), app._show("restore"), app.after(1500, s6)))


def s6():
    shot("r5_restore")
    app.destroy()


app.after(2500, walk)
app.mainloop()
for line in log:
    print(line)
print("остатки удалены:", not local.exists() and not engine._key_exists("HKCU", f"Software\\{NAME}"))
h = engine._load_history()
engine._write_json(engine.HISTORY_FILE, [x for x in h if NAME not in x["name"]])
for f in apps.REG_BACKUP_DIR.glob(f"*_{NAME}.reg"):
    f.unlink()
print("ошибок в окне:", len(errors))
for e in errors:
    print(e)
