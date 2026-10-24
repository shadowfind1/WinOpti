"""WinOpti: gui.py. Окно с вкладками слева (customtkinter). Вся логика твиков в engine.py,
каркас окна в ui/shell.py, каждая вкладка в своём ui/*_page.py.

Запуск:  python gui.py
Нужно:   pip install customtkinter pillow

Фоновая работа: окно кладёт задание ("имя", аргументы…) в self.jobs, фоновый поток вызывает
метод _job_<имя>; если тот упал — _fail_<имя> (если есть). Ответы окну идут в self.results как
("вид", аргументы…) и обрабатываются методами _on_<вид> в потоке окна.
"""
import ctypes
import os
import queue
import sys
import threading
import traceback

import customtkinter as ctk

import engine
from ui.apps_page import AppsMixin, UninstallDialog  # noqa: F401 — UninstallDialog нужен тестам
from ui.cleanup_page import CleanupMixin
from ui.presets_page import PresetsMixin
from ui.restore_page import RestoreMixin
from ui.search_page import SearchMixin
from ui.shell import ShellMixin
from ui.startup_page import StartupMixin
from ui.theme import *  # noqa: F403
from ui.tweaks_page import TweaksMixin

# вкладки с твиками: (id категории из tweaks.json, название, подпись, иконка)
SECTIONS = [
    ("privacy", tr("Приватность"), tr("Что Windows собирает и отправляет о тебе"), "privacy"),
    ("interface", tr("Интерфейс"), tr("Реклама, анимации и отклик интерфейса"), "interface"),
    ("appearance", tr("Внешний вид"), tr("Тема, панель задач и контекстное меню — по вкусу"), "appearance"),
    ("explorer", tr("Проводник"), tr("Как проводник показывает файлы"), "explorer"),
    ("performance", tr("Производительность"), tr("Питание, запуск и фоновые процессы"), "performance"),
    ("gaming", tr("Игры и ввод"), tr("Меньше помех в играх, точная мышь, быстрая клавиатура"), "gaming"),
    ("system", tr("Система"), tr("Обновления и системные настройки"), "system"),
]
# остальные вкладки: (id, название, подпись, иконка)
PRESETS = ("presets", tr("Пресеты"), tr("Готовые наборы твиков: включаются одной кнопкой"), "presets")
APPS = ("apps", tr("Приложения"), tr("Удаление программ вместе с остатками и встроенных приложений Windows"),
        "apps")
STARTUP = ("startup", tr("Автозагрузка"), tr("Программы, которые запускаются вместе с Windows"), "startup")
CLEANUP = ("cleanup", tr("Очистка"), tr("Временные файлы, кэш обновлений и корзина"), "cleanup")
RESTORE = ("restore", tr("Восстановление"), tr("Откат изменений и точки восстановления Windows"), "restore")


def resource(*parts):
    """Файл рядом с программой: в собранном exe — в его папке данных (sys._MEIPASS)."""
    base = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base, *parts)


def relaunch_as_admin():
    """Перезапуск с запросом UAC. True = запрос принят, этот процесс надо закрыть."""
    if getattr(sys, "frozen", False):
        return False  # у собранного exe запрос прав зашивается при сборке
    params = " ".join(f'"{a}"' for a in [os.path.abspath(sys.argv[0])] + sys.argv[1:])
    exe = os.path.join(os.path.dirname(sys.executable), "pythonw.exe")  # без лишнего окна консоли
    if not os.path.exists(exe):
        exe = sys.executable
    rc = ctypes.windll.shell32.ShellExecuteW(None, "runas", exe, params, None, 1)
    return rc > 32


class App(ShellMixin, TweaksMixin, SearchMixin, PresetsMixin, AppsMixin, StartupMixin, CleanupMixin, RestoreMixin,
          ctk.CTk):
    def __init__(self):
        super().__init__()
        ctk.set_appearance_mode("dark")
        self.title(APP_NAME)
        self.geometry("1120x760")
        self.minsize(900, 540)
        self.configure(fg_color=BG)
        try:
            self.attributes("-alpha", 0.0)  # окно плавно проявится, когда всё построено
        except Exception:
            pass

        # каркас и анимации
        self._anims = {}              # ключ анимации -> id after()
        self._indicator_y = None      # где сейчас полоска выбранной вкладки
        self._counter_on = 0          # сколько включено (для плавного счётчика в шапке)
        self._toast_job = None
        self.pages = {}               # вкладка -> страница
        self.nav = {}                 # вкладка -> элементы пункта меню
        self.titles, self.subtitles, self.icons = {}, {}, {}
        self.current = None
        # твики и пресеты
        self.admin = engine.is_admin()
        self.tweaks = engine.load_tweaks()
        self.presets = engine.load_presets(self.tweaks)
        self.rows = {}                # id твика -> TweakRow
        self.cat_rows = {}            # категория -> [TweakRow]
        self.loaded = False           # состояния прочитаны хотя бы раз
        self.restore_done = False     # точка восстановления за этот сеанс уже создавалась
        self.preset_ui = {}           # id пресета -> виджеты карточки
        self.preset_busy = False
        # поиск
        self.search_rows = {}         # id твика -> TweakRow в результатах поиска
        self._search_job = None
        self._search_back = None      # куда вернуться, когда поиск очищен
        # очистка
        self.clean_rows = {}          # id пункта очистки -> виджеты строки
        self.clean_sizes = {}         # id пункта очистки -> байт (после сканирования)
        self.clean_scanned = False
        self.clean_busy = False
        self._clean_shown = 0         # сколько сейчас показано в итоге (для плавного счётчика)
        # автозагрузка
        self.startup_items = {}       # id записи автозагрузки -> запись из startup.list_items()
        self.startup_rows = {}        # id записи -> виджеты строки
        self.startup_loaded = False
        self.startup_loading = False
        # приложения
        self.apps_data = {"programs": None, "store": None}  # списки приложений (None = не загружен)
        self.apps_loading = set()
        self.apps_buttons = {}        # id приложения -> кнопка «Удалить»
        self._apps_search_job = None
        # фоновый поток
        self._working = False         # фоновый поток сейчас что-то делает (смену языка придержим)
        self.jobs = queue.Queue()     # задания фоновому потоку (строго по очереди)
        self.results = queue.Queue()  # ответы фонового потока окну
        threading.Thread(target=self._worker, daemon=True).start()

        self._collect_sections()
        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)
        self._build_sidebar([(tr("БЫСТРЫЙ СТАРТ"), ["presets"]), (tr("ОПТИМИЗАЦИЯ"), self.cats),
                             (tr("ОБСЛУЖИВАНИЕ"), ["apps", "startup", "cleanup"]), (tr("БЕЗОПАСНОСТЬ"), ["restore"])])
        self._build_main()
        self._show(self.sections[0])
        self._render_history()
        self._render_rp(None, None)
        self._render_presets()
        self.after(100, self._poll)
        self._refresh()
        self.after(40, lambda: self._animate("alpha", 280, self._set_alpha, self._unlayer))
        self.after(250, self._set_icon)  # позже собственной иконки customtkinter, иначе он её перезапишет

    def _set_icon(self):
        ico = resource("assets", "winopti.ico")
        if os.path.exists(ico):
            try:
                self.iconbitmap(ico)
            except Exception:
                pass

    def _collect_sections(self):
        by_cat = {}
        for t in self.tweaks.values():
            by_cat.setdefault(t.get("category", "other"), []).append(t)
        self.groups = by_cat
        known = {s[0]: s for s in SECTIONS}
        order = [s[0] for s in SECTIONS if s[0] in by_cat]
        order += [c for c in by_cat if c not in known]  # неизвестные категории в конец
        self.cats = order  # вкладки с твиками
        self.sections = [PRESETS[0]] + order + [APPS[0], STARTUP[0], CLEANUP[0], RESTORE[0]]
        for cat in order:
            _, title, sub, ic = known.get(cat, (cat, cat.title(), "", "system"))
            self.titles[cat], self.subtitles[cat], self.icons[cat] = title, sub, ic
        for sec in (PRESETS, APPS, STARTUP, CLEANUP, RESTORE):
            self.titles[sec[0]], self.subtitles[sec[0]], self.icons[sec[0]] = sec[1:]

    def _build_main(self):
        main = ctk.CTkFrame(self, fg_color=BG, corner_radius=0)
        main.grid(row=0, column=1, sticky="nsew")
        self._build_header(main)
        if not self.admin:
            self._banner(main, tr("Нет прав администратора: изменения недоступны. "
                                  "Закрой окно и запусти от имени администратора."), RED, PILL_RED)
        other = engine.other_user()
        if other:
            self._banner(main, tr("WinOpti запущен не от пользователя {user}, а от другой учётной записи. "
                                  "Настройки интерфейса, проводника и мыши применятся ей, а не {user}. "
                                  "Лучше войти под {user} с правами администратора.").format(user=other),
                         ORANGE, PILL_ORANGE)

        box = ctk.CTkFrame(main, fg_color="transparent")
        box.pack(fill="both", expand=True, padx=(28, 12), pady=(12, 12))
        for cat in self.cats:
            self.pages[cat] = self._build_tweak_page(box, cat)
        self.pages["search"] = self._build_search_page(box)
        self.pages["presets"] = self._build_presets_page(box)
        self.pages["apps"] = self._build_apps_page(box)
        self.pages["startup"] = self._build_startup_page(box)
        self.pages["cleanup"] = self._build_cleanup_page(box)
        self.pages["restore"] = self._build_restore_page(box)
        self._build_toast(main)

    # ---------- фоновый поток (в нём нет обращений к виджетам) ----------

    def _worker(self):
        while True:
            name, *args = self.jobs.get()
            self._working = True
            try:
                getattr(self, f"_job_{name}")(*args)
            except Exception as e:
                self.results.put(("message", tr("Ошибка: {err}").format(err=e), True))
                fail = getattr(self, f"_fail_{name}", None)  # вернуть вкладку из состояния «занято»
                if fail:
                    fail(*args, str(e))
            finally:
                self._working = False

    # ---------- приём ответов в потоке окна ----------

    def _poll(self):
        try:
            while True:
                kind, *args = self.results.get_nowait()
                try:
                    getattr(self, f"_on_{kind}")(*args)
                except Exception as e:  # ошибка одного ответа не должна останавливать приём остальных
                    traceback.print_exc()
                    self._status(tr("Ошибка интерфейса ({kind}): {err}").format(kind=kind, err=e), True)
        except queue.Empty:
            pass
        self.after(100, self._poll)


if __name__ == "__main__":
    if not engine.is_admin() and relaunch_as_admin():
        sys.exit(0)
    App().mainloop()
