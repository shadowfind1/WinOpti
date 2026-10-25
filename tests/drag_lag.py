"""Настоящее перетаскивание окна мышью за заголовок, пока фоновый поток выполняет задание вкладки.
Печатает отставание окна от курсора и самое долгое «замирание» окна. Ничего в системе не меняет.
Запуск: python tests/drag_lag.py [задание ...]   (idle, read_all, clean_scan, apps_list, startup_list, rp_list)"""
import ctypes
import pathlib
import sys
import threading
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))  # корень проекта
import gui

u = ctypes.windll.user32


class RECT(ctypes.Structure):
    _fields_ = [("l", ctypes.c_long), ("t", ctypes.c_long), ("r", ctypes.c_long), ("b", ctypes.c_long)]


JOBS = {
    "idle": None,
    "read_all": ("read_all",),
    "clean_scan": ("clean_scan",),
    "apps_list": ("apps_list", "programs"),
    "startup_list": ("startup_list",),
    "rp_list": ("rp_list",),
    "busy": ("test_busy",),   # чистый Python в фоновом потоке 2 с (как разбор тысяч задач/файлов)
    "walk": ("test_walk",),   # обход C:\Windows\System32, как сканирование очистки под админом
}


def _job_test_busy(self):
    end = time.perf_counter() + 2
    n = 0
    while time.perf_counter() < end:
        n += sum(i * i for i in range(200))


def _job_test_walk(self):
    import cleaner
    cleaner._walk([pathlib.Path(r"C:\Windows\System32")], 0)


gui.App._job_test_busy = _job_test_busy
gui.App._job_test_walk = _job_test_walk
todo = sys.argv[1:] or list(JOBS)
app = gui.App()


def drag(label, retry=True):
    hwnd = u.GetParent(app.winfo_id())
    app.after(0, lambda: (app.deiconify(), app.lift(), app.focus_force()))
    time.sleep(0.3)
    u.SetForegroundWindow(hwnd)
    r = RECT()
    u.GetWindowRect(hwnd, ctypes.byref(r))
    x0, y0 = r.l + 420, r.t + 12  # заголовок окна
    u.SetCursorPos(x0, y0)
    time.sleep(0.2)
    u.mouse_event(0x2, 0, 0, 0, 0)  # нажать
    time.sleep(0.15)
    start_l, lags, last_x, last_change, freeze = r.l, [], None, time.perf_counter(), 0.0
    for i in range(1, 801):  # ~1.6 с, 1 px за ~2 мс, туда и обратно
        dx = i if i <= 400 else 800 - i
        u.SetCursorPos(x0 + dx, y0)
        time.sleep(0.002)
        u.GetWindowRect(hwnd, ctypes.byref(r))
        now = time.perf_counter()
        lags.append(abs(dx - (r.l - start_l)))
        if r.l != last_x:
            last_x, last_change = r.l, now
        else:
            freeze = max(freeze, now - last_change)
    u.mouse_event(0x4, 0, 0, 0, 0)  # отпустить
    if lags[len(lags) // 2] > 100 and retry:  # окно не захватилось (не на переднем плане) — ещё раз
        time.sleep(0.5)
        return drag(label, retry=False)
    lags.sort()
    print(f"  {label:14} отставание: медиана {lags[len(lags) // 2]:3} px, худшее {lags[-1]:3} px; "
          f"самое долгое замирание окна {freeze * 1000:4.0f} мс", flush=True)


def run_next():
    if not todo:
        app.after(300, app.destroy)
        return
    name = todo.pop(0)
    if JOBS[name]:
        app.jobs.put(JOBS[name])
        time.sleep(0.05)

    def work():
        drag(name)
        while app._working:  # дождаться конца задания, чтобы следующее мерить отдельно
            time.sleep(0.1)
        time.sleep(0.5)
        app.after(0, run_next)
    threading.Thread(target=work, daemon=True).start()


app.after(4500, lambda: (app.geometry("1100x720+100+60"), app.after(1500, run_next)))
app.mainloop()
