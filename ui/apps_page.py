"""winopt/ui/apps_page.py: вкладка «Приложения» — удаление программ с остатками (мастер UninstallDialog)
и встроенных приложений Windows."""
import queue
import threading
import time
import tkinter
from tkinter import messagebox

import customtkinter as ctk

import apps
import cleaner
from ui.shell import clear
from ui.theme import *  # noqa: F403

APPS_MODES = {tr("Программы"): "programs", tr("Встроенные Windows"): "store"}
APPS_LIMIT = 60  # больше строк customtkinter рисует заметно медленно: дальше просим уточнить поиск


class UninstallDialog(ctk.CTkToplevel):
    """Мастер удаления программы: деинсталлятор -> ожидание -> остатки -> результат."""

    def __init__(self, app, prog):
        super().__init__(app)
        self.app, self.prog = app, prog
        self.q = queue.Queue()      # ответы фоновых потоков мастера
        self.waiting = False        # ждём, пока деинсталлятор удалит программу
        self.gone_at = None
        self.logged = False
        self.vars = []
        self.title(f"{APP_NAME}: {tr('удаление программы')}")
        self.geometry("780x600")
        self.minsize(620, 440)
        self.configure(fg_color=BG)
        self.transient(app)
        self.protocol("WM_DELETE_WINDOW", self._close)
        ctk.CTkLabel(self, text=prog["name"], anchor="w", justify="left", wraplength=720, text_color=TEXT,
                     font=font(20, True)).pack(fill="x", padx=24, pady=(20, 0))
        self.text = ctk.CTkLabel(self, text="", anchor="w", justify="left", wraplength=720, text_color=MUTED,
                                 font=font(12))
        self.text.pack(fill="x", padx=24, pady=(6, 0))
        self.buttons = ctk.CTkFrame(self, fg_color="transparent")
        self.buttons.pack(side="bottom", fill="x", padx=24, pady=(10, 18))
        self.body = ctk.CTkScrollableFrame(self, fg_color="transparent", scrollbar_button_color="#2a3040",
                                           scrollbar_button_hover_color="#3a4358")
        self.body.pack(fill="both", expand=True, padx=(24, 12), pady=(12, 0))
        self._step_confirm()
        self.after(150, self._grab)
        self.after(700, self._poll)

    def _grab(self):
        try:
            self.grab_set()  # модальное окно: основное окно не трогаем, пока идёт удаление
            self.lift()
            self.focus_force()
        except Exception:
            pass

    def _set(self, text, color=MUTED):
        self.text.configure(text=text, text_color=color)
        clear(self.body)
        clear(self.buttons)

    def _btn(self, text, kind, command, side="left"):
        b = button(self.buttons, text, kind, command)
        b.pack(side=side, padx=(0, 10) if side == "left" else 0)
        return b

    def _line(self, text, color=MUTED):
        ctk.CTkLabel(self.body, text=text, anchor="w", justify="left", wraplength=700, text_color=color,
                     font=font(12)).pack(fill="x", pady=3)

    def _log(self):
        if not self.logged:
            self.logged = True
            apps.log_uninstall(self.prog["name"])

    # ---------- шаги ----------

    def _step_confirm(self):
        p = self.prog
        self._set(tr("Сначала запустится деинсталлятор самой программы: пройди его до конца. Потом WinOpti найдёт "
                     "оставшиеся папки, ярлыки и записи реестра и покажет их списком. Удалится только то, что ты "
                     "отметишь: файлы — в корзину, реестр — с резервной копией."))
        size = cleaner.fmt_size(p["size"]) if p["size"] else ""
        meta = " · ".join(x for x in (p["publisher"], p["version"], size) if x)
        if meta:
            self._line(meta)
        if p["location"]:
            self._line(tr("Папка: {path}").format(path=p["location"]))
        if p["hint"] == "keep":
            self._line(tr("Внимание: {msg}.").format(msg=p["hint_text"]), ORANGE)
        self._line(tr("«Только найти остатки» — если деинсталлятор не работает или программа уже удалена."), DIM)
        start = self._btn(tr("Запустить деинсталлятор"), "primary", self._start)
        if not (p["uninstall"] or p["msi"]):
            start.configure(state="disabled")
        self._btn(tr("Только найти остатки"), "secondary", self._scan)
        self._btn(tr("Отмена"), "secondary", self._close, side="right")

    def _start(self):
        try:
            apps.run_uninstaller(self.prog)
        except Exception as e:
            self._set(tr("Деинсталлятор не запустился: {err}. Можно сразу поискать остатки.").format(err=e), RED)
            self._btn(tr("Найти остатки"), "primary", self._scan)
            self._btn(tr("Закрыть"), "secondary", self._close, side="right")
            return
        self._set(tr("Деинсталлятор запущен (его окно могло открыться позади WinOpti). Пройди его до конца: "
                     "WinOpti сам заметит, что программа удалилась, и начнёт искать остатки."))
        self.waiting, self.gone_at = True, None
        self._btn(tr("Деинсталлятор закрыт — искать остатки"), "primary", self._scan)
        self._btn(tr("Отмена"), "secondary", self._close, side="right")

    def _scan(self):
        self.waiting = False
        self._set(tr("Ищу остатки…"))
        threading.Thread(target=self._scan_worker, daemon=True).start()

    def _scan_worker(self):
        try:
            all_apps = self.app.apps_data.get("programs") or apps.list_programs()
            items, forced = apps.scan_leftovers(self.prog, all_apps)
            self.q.put(("scanned", items, forced, None))
        except Exception as e:
            self.q.put(("scanned", [], False, str(e)))

    def _step_results(self, items, forced, err):
        if err:
            self._set(tr("Не удалось найти остатки: {err}").format(err=err), RED)
            self._btn(tr("Закрыть"), "secondary", self._close, side="right")
            return
        if not forced:
            self._log()  # программы больше нет в списке установленных
        if not items:
            if forced:
                self._set(tr("Программа всё ещё установлена, а остатков по имени не найдено."), ORANGE)
            else:
                self._set(tr("Готово: программа удалена, остатков не найдено."), GREEN)
            self._btn(tr("Готово"), "primary", self._close)
            return
        total = cleaner.fmt_size(sum(i["size"] or 0 for i in items))
        if forced:
            self._set(tr("Похоже, программа НЕ удалена: деинсталлятор отменён или завершился с ошибкой. Ниже — сама "
                         "программа целиком. Галочки сняты: отмечай, только если хочешь удалить её принудительно."),
                      ORANGE)
        else:
            self._set(tr("Программа удалена. Найдено остатков: {n}, примерно {size}. Пункты без галочки WinOpti "
                         "не уверен, что они относятся только к этой программе: проверь их сам."
                         ).format(n=len(items), size=total))
        self.vars = []
        kinds = {"dir": tr("папка"), "file": tr("файл"), "reg": tr("реестр")}
        for it in items:
            var = ctk.BooleanVar(value=it["checked"])
            row = ctk.CTkFrame(self.body, fg_color=CARD_ALT, corner_radius=10)
            row.pack(fill="x", pady=3, padx=(0, 6))
            row.columnconfigure(1, weight=1)
            ctk.CTkCheckBox(row, text="", variable=var, width=24, command=self._update_remove_btn,
                            fg_color=ACCENT, hover_color=ACCENT_HOVER, border_color="#3a4358",
                            checkbox_width=20, checkbox_height=20
                            ).grid(row=0, column=0, rowspan=2, padx=(12, 4))
            ctk.CTkLabel(row, text=it["path"], anchor="w", justify="left", wraplength=600, text_color=TEXT,
                         font=font(12)).grid(row=0, column=1, sticky="w", padx=(4, 12), pady=(8, 0))
            size = f" · {cleaner.fmt_size(it['size'])}" if it["size"] else ""
            ctk.CTkLabel(row, text=f"{kinds[it['kind']]}{size} · {it['why']}", anchor="w", justify="left",
                         wraplength=600, text_color=MUTED, font=font(11)
                         ).grid(row=1, column=1, sticky="w", padx=(4, 12), pady=(0, 8))
            self.vars.append((var, it))
        self.remove_btn = self._btn("", "danger", self._remove)
        self._btn(tr("Закрыть, ничего не удалять"), "secondary", self._close, side="right")
        self._update_remove_btn()

    def _update_remove_btn(self):
        sel = [it for v, it in self.vars if v.get()]
        size = cleaner.fmt_size(sum(it["size"] or 0 for it in sel))
        self.remove_btn.configure(text=tr("Удалить выбранное ({n}, {size})").format(n=len(sel), size=size),
                                  state="normal" if sel else "disabled")

    def _remove(self):
        sel = [it for v, it in self.vars if v.get()]
        self._set(tr("Удаляю…"))
        threading.Thread(target=self._remove_worker, args=(sel,), daemon=True).start()

    def _remove_worker(self, sel):
        try:
            res = apps.remove_leftovers(sel, self.prog["name"])
        except Exception as e:
            res = {"removed": 0, "freed": 0, "errors": [str(e)], "backup": None}
        self.q.put(("removed", res))

    def _step_done(self, res):
        text = tr("Удалено пунктов: {n}, освобождено {size}. Файлы — в корзине.").format(
            n=res["removed"], size=cleaner.fmt_size(res["freed"]))
        if res["backup"]:
            text += "\n" + tr("Резервная копия реестра: {path}\nЧтобы вернуть реестр, открой этот файл двойным "
                              "кликом.").format(path=res["backup"])
        self._set(text, GREEN if not res["errors"] else ORANGE)
        for e in res["errors"]:
            self._line(e, RED)
        if res["removed"] and not apps.still_installed(self.prog):
            self._log()
        self._btn(tr("Готово"), "primary", self._close)

    def _close(self):
        self.app._apps_after_uninstall()
        self.destroy()

    def _poll(self):
        try:
            if not self.winfo_exists():
                return
        except Exception:
            return
        if self.waiting and not apps.still_installed(self.prog):
            self.gone_at = self.gone_at or time.time()
            if time.time() - self.gone_at > 2:  # деинсталлятор мог ещё дочищать файлы
                self._scan()
        try:
            while True:
                msg = self.q.get_nowait()
                if msg[0] == "scanned":
                    self._step_results(*msg[1:])
                elif msg[0] == "removed":
                    self._step_done(msg[1])
        except queue.Empty:
            pass
        self.after(700, self._poll)


class AppsMixin:

    def _build_apps_page(self, box):
        page = ctk.CTkFrame(box, fg_color="transparent")
        bar = ctk.CTkFrame(page, fg_color="transparent")
        bar.pack(fill="x", padx=(0, 26), pady=(0, 8))
        self.apps_mode = ctk.CTkSegmentedButton(
            bar, values=list(APPS_MODES), command=lambda _v: self._apps_show(), height=34, font=font(13, True),
            fg_color="#232938", selected_color=ACCENT, selected_hover_color=ACCENT_HOVER,
            unselected_color="#232938", unselected_hover_color="#2d3548", text_color=TEXT)
        self.apps_mode.set(next(iter(APPS_MODES)))
        self.apps_mode.pack(side="left")
        self.apps_search = ctk.CTkEntry(bar, placeholder_text=tr("Поиск по названию или издателю"), width=230,
                                        height=34, fg_color=CARD, border_color=BORDER, text_color=TEXT,
                                        font=font(12))
        self.apps_search.pack(side="left", padx=(12, 0))
        self.apps_search.bind("<KeyRelease>", lambda _e: self._apps_search_changed())
        self.apps_reload_btn = button(bar, tr("Обновить"), "secondary", self._apps_reload, width=104, height=34)
        self.apps_reload_btn.pack(side="right")
        self.apps_count = ctk.CTkLabel(bar, text="", text_color=MUTED, font=font(12))
        self.apps_count.pack(side="right", padx=12)
        scroll = self._scroll(page)
        self.apps_box = ctk.CTkFrame(scroll, fg_color="transparent")
        self.apps_box.pack(fill="x")
        return page

    def _refresh_apps(self):
        self._apps_reload()

    def _on_show_apps(self):
        self._apps_show()

    def _on_hide_apps(self):
        """Список и так перестраивается при каждом открытии, а скрытым он держал бы ~500 окон Windows,
        с которыми тормозит перетаскивание окна."""
        job = self._chunk_jobs.pop("apps", None)
        if job:
            self.after_cancel(job)
        self._clear(self.apps_box)
        self.apps_buttons = {}

    def _apps_mode_key(self):
        return APPS_MODES[self.apps_mode.get()]

    def _apps_show(self):
        mode = self._apps_mode_key()
        if self.apps_data[mode] is None:
            self._apps_load(mode)
        self._apps_render()

    def _apps_reload(self):
        mode = self._apps_mode_key()
        self.apps_data[mode] = None
        self._apps_show()

    def _apps_load(self, mode):
        if mode not in self.apps_loading:
            self.apps_loading.add(mode)
            self.jobs.put(("apps_list", mode))

    def _apps_search_changed(self):
        if self._apps_search_job:
            self.after_cancel(self._apps_search_job)
        self._apps_search_job = self.after(300, self._apps_render)  # не перерисовывать на каждую букву

    def _apps_render(self, err=None):
        self._apps_search_job = None
        mode = self._apps_mode_key()
        self._clear(self.apps_box)
        self.apps_buttons = {}
        items = self.apps_data[mode]
        if items is None:
            self.apps_count.configure(text="")
            if err:
                self._note(self.apps_box, tr("Не удалось получить список: {err}").format(err=err), RED)
            else:
                self._note(self.apps_box, tr("Загружаю список…"))
            return
        q = self.apps_search.get().strip().lower()
        shown = [a for a in items if not q or q in a["name"].lower()
                 or q in a.get("publisher", "").lower() or q in a.get("package", "").lower()]
        self.apps_count.configure(text=tr("{shown} из {total}").format(shown=len(shown), total=len(items)))
        if mode == "store":
            self._note(self.apps_box, tr("Приложения удаляются для всех пользователей и не возвращаются после "
                                         "обновлений Windows. Вернуть любое можно из Microsoft Store."))
        else:
            self._note(self.apps_box, tr("Сначала запускается деинсталлятор программы, потом WinOpti находит "
                                         "оставшиеся папки, ярлыки и записи реестра. Файлы удаляются в корзину, "
                                         "реестр — с резервной копией."))
        def done():
            if len(shown) > APPS_LIMIT:
                self._note(self.apps_box, tr("Показаны первые {n}. Уточни поиск, чтобы увидеть остальные."
                                             ).format(n=APPS_LIMIT))
        self._build_in_chunks("apps", shown[:APPS_LIMIT], lambda a: self._apps_row(a, mode), done=done)

    def _apps_row(self, a, mode):
        row = ctk.CTkFrame(self.apps_box, corner_radius=12, fg_color=CARD, border_width=1, border_color=BORDER)
        row.pack(fill="x", padx=(0, 10), pady=4)
        row.columnconfigure(0, weight=1)
        # простые надписи Tk вместо CTkLabel: строк до сотни, а у каждой CTkLabel — три окна Windows
        head = tkinter.Frame(row, bg=CARD)
        head.grid(row=0, column=0, sticky="w", padx=18, pady=(11, 0))
        text(head, a["name"], 14, True).pack(side="left")
        if a["hint"] == "remove":
            text(head, "   ·   " + tr("можно удалить"), 11, True, GREEN).pack(side="left")
        elif a["hint"] == "keep":
            text(head, "   ·   " + tr("лучше не трогать"), 11, True, ORANGE).pack(side="left")
        if mode == "programs":
            size = cleaner.fmt_size(a["size"]) if a["size"] else ""
            meta = " · ".join(x for x in (a["publisher"], a["version"], size, a["date"]) if x)
        else:
            meta = a["package"]
        if a["hint_text"]:
            meta += f"  —  {a['hint_text']}"
        autowrap(text(row, meta, 12, color=MUTED, wrap=620), row, 160
                 ).grid(row=1, column=0, sticky="w", padx=18, pady=(3, 11))
        btn = button(row, tr("Удалить"), "danger", lambda: self._apps_remove(a, mode), width=100, height=32)
        btn.grid(row=0, column=1, rowspan=2, padx=14)
        if not self.admin:
            btn.configure(state="disabled")
        self.apps_buttons[a["id"]] = btn

    def _apps_remove(self, a, mode):
        if mode == "programs":
            UninstallDialog(self, a)
            return
        text = tr("Удалить «{name}»?\n\nПриложение удалится для всех пользователей и не вернётся после "
                  "обновлений. Вернуть его можно из Microsoft Store.").format(name=a["name"])
        if a["hint"] == "keep":
            text += "\n\n" + tr("Внимание: {msg}.").format(msg=a["hint_text"] or tr("это приложение лучше оставить"))
        if not messagebox.askyesno(APP_NAME, text, parent=self):
            return
        btn = self.apps_buttons.get(a["id"])
        if btn:
            btn.configure(state="disabled", text=tr("Удаляю…"))
        self._status(tr("Удаляю «{name}»…").format(name=a["name"]))
        self.jobs.put(("store_remove", a["id"]))

    def _apps_after_uninstall(self):
        """После мастера удаления: список программ и автозагрузка могли измениться."""
        self.apps_data["programs"] = None
        self.startup_loaded = False
        if self.current == "apps" and self._apps_mode_key() == "programs":
            self._apps_show()
        self._render_history()

    # ---------- фоновый поток ----------

    def _job_apps_list(self, mode):
        items = apps.list_programs() if mode == "programs" else apps.list_store_apps()
        self.results.put(("apps_list", mode, items, None))

    def _fail_apps_list(self, mode, error):
        self.results.put(("apps_list", mode, None, error))

    def _job_store_remove(self, package):
        try:
            apps.remove_store_app(package)
            err = None
        except Exception as e:
            err = str(e)
        self.results.put(("store_removed", package, err))

    # ---------- ответы ----------

    def _on_apps_list(self, mode, items, err):
        self.apps_loading.discard(mode)
        self.apps_data[mode] = items
        if self.current == "apps" and self._apps_mode_key() == mode:
            self._apps_render(err)

    def _on_store_removed(self, package, err):
        store = self.apps_data["store"] or []
        a = next((x for x in store if x["id"] == package), {"name": package})
        if err:
            self._status(tr("«{name}» не удалено: {err}").format(name=a["name"], err=err), True)
            btn = self.apps_buttons.get(package)
            if btn:
                btn.configure(state="normal", text=tr("Удалить"))
        else:
            self._status(tr("«{name}» удалено.").format(name=a["name"]))
            self.apps_data["store"] = [x for x in store if x["id"] != package]
            if self.current == "apps" and self._apps_mode_key() == "store":
                self._apps_render()
            self._render_history()
