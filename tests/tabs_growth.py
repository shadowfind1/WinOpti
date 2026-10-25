"""Растёт ли «вес» окна с каждой открытой вкладкой: число окон Windows и шаг перемещения окна
после каждой вкладки. Ничего в системе не меняет. Запуск: python tests/tabs_growth.py"""
import ctypes
import pathlib
import sys
import time
import traceback

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))  # корень проекта
import gui

u = ctypes.windll.user32
errors = []
app = gui.App()
app.report_callback_exception = lambda *a: errors.append("".join(traceback.format_exception(*a)))
ORDER = ["interface", "performance", "gaming", "system", "startup", "cleanup", "restore",
         "privacy", "cleanup", "startup", "restore"]


def hwnds():
    n = [0]
    cb = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)(
        lambda h, l: n.__setitem__(0, n[0] + 1) or True)
    u.EnumChildWindows(u.GetParent(app.winfo_id()), cb, 0)
    return n[0]


def move_cost():
    xs = []
    for i in range(30):
        s = time.perf_counter()
        app.geometry(f"+{100 + (i % 10) * 5}+60")
        app.update()
        xs.append((time.perf_counter() - s) * 1000)
    return sorted(xs)[15]


def step(i=0):
    if i:
        print(f"  {i:2}. {app.current:12} окон Windows {hwnds():5}   шаг перемещения {move_cost():4.1f} мс", flush=True)
    if i == len(ORDER):
        app.after(200, app.destroy)
        return
    app._show(ORDER[i])
    app.after(3000, lambda: step(i + 1))


app.after(3000, lambda: (app.geometry("1100x720+100+60"),
                         print(f"   0. {app.current:12} окон Windows {hwnds():5}   шаг перемещения {move_cost():4.1f} мс"),
                         step()))
app.mainloop()
print("ошибок в окне:", len(errors))
for e in errors:
    print(e)
