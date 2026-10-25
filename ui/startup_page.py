"""winopt/ui/startup_page.py: вкладка «Автозагрузка» — что запускается вместе с Windows."""
import tkinter

import customtkinter as ctk

import startup
from ui.theme import *  # noqa: F403


class StartupMixin:

    def _build_startup_page(self, box):
        page = ctk.CTkFrame(box, fg_color="transparent")
        scroll = self._scroll(page)
        card = self._card(scroll, tr("Что запускается вместе с Windows"),
                          tr("Чем меньше программ стартует при входе, тем быстрее загрузка и больше свободной "
                             "памяти. Выключенная программа не удаляется: её можно включить обратно в любой момент, "
                             "и она так же выключена в Диспетчере задач."))
        top = ctk.CTkFrame(card, fg_color="transparent")
        top.pack(fill="x", padx=20, pady=(14, 16))
        top.columnconfigure(0, weight=1)
        self.startup_total = ctk.CTkLabel(top, text="—", anchor="w", text_color=TEXT, font=font(30, True))
        self.startup_total.grid(row=0, column=0, sticky="w")
        ctk.CTkLabel(top, text=tr("программ запускается с Windows"), anchor="w", text_color=MUTED, font=font(12)
                     ).grid(row=1, column=0, sticky="w")
        self.startup_reload_btn = button(top, tr("Обновить список"), "secondary", self._startup_load, width=150)
        self.startup_reload_btn.grid(row=0, column=1, rowspan=2)
        self.startup_box = ctk.CTkFrame(scroll, fg_color="transparent")
        self.startup_box.pack(fill="x")
        if self.startup_loaded:  # список уже читали — показываем его, не читая заново
            self._render_startup(*self._startup_last)
        if self.startup_loading:
            self.startup_reload_btn.configure(state="disabled")
            self.startup_total.configure(text="…")
        return page

    def _on_drop_startup(self):
        self.startup_rows = {}

    def _refresh_startup(self):
        self._startup_load()

    def _on_show_startup(self):
        if not self.startup_loaded:
            self._startup_load()

    def _render_startup(self, items, warning):
        self._clear(self.startup_box)
        self.startup_rows = {}
        if items is None:
            self._note(self.startup_box, tr("Не удалось прочитать автозагрузку: {err}").format(err=warning), RED)
            return
        if warning:
            self._note(self.startup_box, warning, ORANGE)
        if not items:
            self._note(self.startup_box, tr("Автозагрузка пуста."))
        self.startup_items = {it["id"]: it for it in items}
        for it in items:
            row = ctk.CTkFrame(self.startup_box, corner_radius=14, fg_color=CARD, border_width=1,
                               border_color=BORDER)
            row.pack(fill="x", padx=(0, 10), pady=5)
            row.columnconfigure(0, weight=1)
            # простые надписи Tk вместо CTkLabel: у каждой CTkLabel три окна Windows (см. theme.text)
            head = tkinter.Frame(row, bg=CARD)
            head.grid(row=0, column=0, sticky="w", padx=18, pady=(12, 0))
            text(head, it["name"], 14, True).pack(side="left")
            if it["hint"] == "disable":
                text(head, "   ·   " + tr("можно выключить"), 11, True, GREEN).pack(side="left")
            elif it["hint"] == "keep":
                text(head, "   ·   " + tr("лучше не трогать"), 11, True, ORANGE).pack(side="left")
            meta = " · ".join(x for x in (it["company"], it["where"]) if x)
            if it["hint_text"]:
                meta += f"  —  {it['hint_text']}"
            autowrap(text(row, meta, 12, color=MUTED, wrap=620), row, 120
                     ).grid(row=1, column=0, sticky="w", padx=18, pady=(3, 0))
            cmd = it["command"] if len(it["command"]) <= 110 else it["command"][:107] + "…"
            text(row, cmd, 11, color=DIM).grid(row=2, column=0, sticky="w", padx=18, pady=(3, 0))
            status = text(row, "", 11, color=MUTED)
            status.grid(row=3, column=0, sticky="w", padx=18, pady=(2, 10))
            if it["missing"]:
                status.configure(text=tr("Файл программы не найден: запись осталась от удалённой программы, "
                                         "её можно выключить."), fg=RED)
            elif it["admin"] and not self.admin:
                status.configure(text=tr("Нужны права администратора"))
            switch = ctk.CTkSwitch(row, text="", width=50, progress_color=ACCENT, fg_color="#2b3243",
                                   button_color="#e8ebf2", button_hover_color="#ffffff",
                                   command=lambda i=it["id"]: self._startup_toggle(i))
            switch.grid(row=0, column=1, rowspan=4, padx=20)
            if it["enabled"]:
                switch.select()
            if it["admin"] and not self.admin:
                switch.configure(state="disabled")
            self.startup_rows[it["id"]] = {"switch": switch, "status": status}
        self._startup_update_total()

    def _startup_update_total(self):
        items = self.startup_items.values()
        on = sum(1 for it in items if it["enabled"])
        if self.current == "startup":
            self.startup_total.configure(text=tr("{on} из {total}").format(on=on, total=len(items))
                                         if self.startup_loaded else "…")
        self._badge("startup", str(on) if self.startup_loaded else "")

    def _startup_load(self):
        if self.startup_loading:
            return
        self.startup_loading = True
        if self.current == "startup":
            self.startup_reload_btn.configure(state="disabled")
            self.startup_total.configure(text="…")
        self.jobs.put(("startup_list",))

    def _startup_toggle(self, sid):
        row = self.startup_rows[sid]
        enable = bool(row["switch"].get())
        row["switch"].configure(state="disabled")
        row["status"].configure(text=tr("включаю…") if enable else tr("выключаю…"), fg=MUTED)
        self.jobs.put(("startup_set", sid, enable))

    # ---------- фоновый поток ----------

    def _job_startup_list(self):
        items, warning = startup.list_items()
        self.results.put(("startup_list", items, warning))

    def _fail_startup_list(self, error):
        self.results.put(("startup_list", None, error))

    def _job_startup_set(self, sid, enable):
        it = self.startup_items[sid]
        try:
            startup.set_enabled(it, enable)
            err = None
        except Exception as e:
            err = str(e)
        try:
            state = startup.is_enabled(it)
        except Exception:
            state = it["enabled"]
        self.results.put(("startup_set", sid, state, err))

    # ---------- ответы ----------

    def _on_startup_list(self, items, warning):
        self.startup_loaded = True
        self.startup_loading = False
        self._startup_last = (items, warning)
        if self.current == "startup":
            self.startup_reload_btn.configure(state="normal")
            self._render_startup(items, warning)
        else:  # вкладку уже закрыли: запомнить список, нарисуем при открытии
            self.startup_items = {it["id"]: it for it in items or []}
            self._startup_update_total()

    def _on_startup_set(self, sid, state, err):
        it, row = self.startup_items[sid], self.startup_rows.get(sid)
        it["enabled"] = state
        if row:
            row["switch"].select() if state else row["switch"].deselect()
            row["switch"].configure(state="normal")
            row["status"].configure(text=tr("Ошибка: {err}").format(err=err) if err else "",
                                    fg=RED if err else MUTED)
        if err:
            self._status(tr("{name}: ошибка: {err}").format(name=it["name"], err=err), True)
        elif state:
            self._status(tr("{name}: будет запускаться вместе с Windows.").format(name=it["name"]))
        else:
            self._status(tr("{name}: больше не запускается вместе с Windows.").format(name=it["name"]))
        self._startup_update_total()
        self._render_history()
