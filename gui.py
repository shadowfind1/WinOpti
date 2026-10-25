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
    ("privacy", tr("Телеметрия и фон"), tr("Слежка и фоновые службы, которые тратят ресурсы ПК"), "privacy"),
    ("interface", tr("Интерфейс"), tr("Анимации, прозрачность и отклик интерфейса"), "interface"),
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
        # Фоновый поток (чтение задач планировщика, сканирование очистки, реестр) держит GIL, а окну на каждом
        # шаге перетаскивания нужно выполнить немного Python. По умолчанию поток отдаёт GIL раз в 5 мс, и окно
        # замирало при перетаскивании до ~350 мс; с 1 мс — не больше ~30 мс.
        sys.setswitchinterval(0.001)
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
        self._chunk_jobs = {}         # ключ -> id after() недостроенной порции карточек
        self._fit_jobs = {}           # прокручиваемая область -> отложенная подгонка ширины (см. _fit_scroll)
        self._fit_pending = set()     # области скрытых страниц, ширину которых подгоним при открытии
        self._resizing_until = 0.0   # до какого момента считаем, что окно растягивают
        self._moving_until = 0.0      # до какого момента считаем, что окно двигают (дорисовка ждёт)
        self._root_geometry = None
        self.bind("<Configure>", self._on_root_configure, add="+")
        self._indicator_y = None      # где сейчас полоска выбранной вкладки
        self.progress_rows = {}       # вкладка с твиками -> строка «N из M включено» (см. _progress_row)
        self._toast_job = None
        self.pages = {}               # вкладка -> страница
        self.nav = {}                 # вкладка -> элементы пункта меню
        self.titles, self.subtitles, self.icons = {}, {}, {}
        self.current = None
        # твики и пресеты
        self.admin = engine.is_admin()
        self.tweaks = engine.load_tweaks()
        self.presets = engine.load_presets(self.tweaks)
        # состояние твиков хранится отдельно от карточек: карточки создаются лениво, только для открытых вкладок
        self.states = {tid: "loading" for tid in self.tweaks}  # id твика -> on/off/partial/n/a/error/loading
        self.busy = {}                # id твика -> текст «применяю…», пока идёт операция
        self.rows = {}                # id твика -> TweakRow (только для уже построенных вкладок)
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
        self._rp_last = (None, None)  # последний список точек восстановления (страница может быть не построена)
        self._rp_drawn = None         # какой список точек сейчас нарисован
        self._history_sig = None      # какие изменения и журнал сейчас нарисованы на «Восстановлении»
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
        self.after(100, self._poll)
        self._refresh()
        self.after(10, self._no_full_redraw)
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
            if not engine.is_hidden(t):  # скрытые — только для отката на «Восстановлении»
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

        # страницы строятся лениво, при первом открытии (см. ShellMixin._page): все сразу — это ~2400
        # виджетов, 7 секунд запуска и заметно более медленное перетаскивание окна
        self.page_box = ctk.CTkFrame(main, fg_color="transparent")
        self.page_box.pack(fill="both", expand=True, padx=(28, 12), pady=(12, 12))
        self._builders = {cat: (lambda c=cat: self._build_tweak_page(self.page_box, c)) for cat in self.cats}
        self._builders.update({
            "search": lambda: self._build_search_page(self.page_box),
            "presets": lambda: self._build_presets_page(self.page_box),
            "apps": lambda: self._build_apps_page(self.page_box),
            "startup": lambda: self._build_startup_page(self.page_box),
            "cleanup": lambda: self._build_cleanup_page(self.page_box),
            "restore": lambda: self._build_restore_page(self.page_box),
        })
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
