"""Регрессионный прогон WinOpti в окне (без прав администратора): все вкладки, уведомление, скриншоты.
Ничего в системе не меняет. Аргумент: папка для скриншотов."""
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


steps = list(app.sections)


def walk():
    if steps:
        app._show(steps.pop(0))
        app.after(600, walk)
        return
    hidden = [c for c, it in app.nav.items() if it["wrap"].winfo_height() <= 1]
    log.append(f"вкладки: {', '.join(app.sections)}")
    log.append(f"пункты меню без места: {hidden or 'нет'}")
    app._show("gaming")
    app.after(900, s2)


def s2():
    app._status(gui.tr("{name}: готово.").format(name="Test"))
    app.after(700, lambda: (shot("r1_gaming"), app._show("startup"), app.after(2500, s3)))


def s3():
    shot("r2_startup")
    app._show("cleanup")
    app.after(3000, lambda: (shot("r3_cleanup"), app._show("restore"), app.after(2500, s4)))


def s4():
    shot("r4_restore")
    app.destroy()


app.after(2500, walk)
app.after(90000, app.destroy)  # не виснуть, если в окне ошибка
app.mainloop()
for line in log:
    print(line)
print("ошибок в окне:", len(errors))
for e in errors:
    print(e)
