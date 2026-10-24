"""winopt/ui/restore_page.py: вкладка «Восстановление» — откат изменений WinOpti, точки восстановления
Windows и журнал."""
from datetime import datetime
from tkinter import messagebox

import customtkinter as ctk

import engine
from i18n import loc
from ui.theme import *  # noqa: F403

JOURNAL = {"on": (tr("включено"), PILL_GREEN, GREEN), "off": (tr("откачено"), PILL_GRAY, MUTED),
           "clean": (tr("очистка"), PILL_BLUE, ACCENT),
           "startup_off": (tr("автозапуск выкл."), PILL_BLUE, ACCENT),
           "startup_on": (tr("автозапуск вкл."), PILL_GRAY, MUTED),
           "uninstall": (tr("удалено"), PILL_RED, RED)}


class RestoreMixin:

    def _build_restore_page(self, box):
        page = ctk.CTkFrame(box, fg_color="transparent")
        scroll = self._scroll(page)

        # 1. изменения WinOpti
        card = self._card(scroll, tr("Изменения WinOpti"),
                          tr("Твики, которые ты включил через WinOpti. Откат возвращает значения, "
                             "которые были до включения."))
        bar = ctk.CTkFrame(card, fg_color="transparent")
        bar.pack(fill="x", padx=20, pady=(12, 0))
        self.revert_all_btn = button(bar, tr("Откатить всё"), "danger", self._revert_all, width=130)
        self.revert_all_btn.pack(side="left")
        self.applied_box = ctk.CTkFrame(card, fg_color="transparent")
        self.applied_box.pack(fill="x", padx=20, pady=(8, 16))

        # 2. точки восстановления Windows
        card = self._card(scroll, tr("Точки восстановления Windows"),
                          tr("Точка — это снимок системы. Если после изменений что-то пошло не так, "
                             "Windows можно вернуть к выбранной точке: нажми «Открыть восстановление системы» "
                             "и выбери её в списке."))
        bar = ctk.CTkFrame(card, fg_color="transparent")
        bar.pack(fill="x", padx=20, pady=(12, 0))
        self.rp_create_btn = button(bar, tr("Создать точку сейчас"), "primary", self._create_restore_point)
        self.rp_create_btn.pack(side="left")
        button(bar, tr("Открыть восстановление системы"), "secondary", self._open_restore
               ).pack(side="left", padx=(10, 0))
        self.rp_box = ctk.CTkFrame(card, fg_color="transparent")
        self.rp_box.pack(fill="x", padx=20, pady=(8, 16))

        # 3. журнал
        card = self._card(scroll, tr("Журнал"))
        self.journal_box = ctk.CTkFrame(card, fg_color="transparent")
        self.journal_box.pack(fill="x", padx=20, pady=(8, 16))
        return page

    def _on_show_restore(self):
        self._render_history()

    def _render_history(self):
        # применённые изменения
        items = engine.get_applied(self.tweaks)
        self._clear(self.applied_box)
        if not items:
            self._note(self.applied_box, tr("Пока ничего не применено через WinOpti."))
        for it in items:
            row = ctk.CTkFrame(self.applied_box, fg_color=CARD_ALT, corner_radius=10)
            row.pack(fill="x", pady=4)
            row.columnconfigure(0, weight=1)
            ctk.CTkLabel(row, text=loc(self.tweaks[it["id"]], "name"), anchor="w", text_color=TEXT,
                         font=font(13, True)).grid(row=0, column=0, sticky="w", padx=16, pady=(10, 0))
            when = tr("применено {time}").format(time=it["time"]) if it["time"] else tr("применено")
            ctk.CTkLabel(row, text=when, anchor="w", text_color=MUTED, font=font(11)
                         ).grid(row=1, column=0, sticky="w", padx=16, pady=(0, 10))
            btn = button(row, tr("Откатить"), "secondary", lambda tid=it["id"]: self._revert_one(tid),
                         width=100, height=32)
            btn.grid(row=0, column=1, rowspan=2, padx=14)
            if not self.admin:
                btn.configure(state="disabled")
        self.revert_all_btn.configure(state="normal" if items and self.admin else "disabled")

        # журнал: имена твиков показываем на текущем языке, остальные записи — как записаны
        self._clear(self.journal_box)
        events = engine.get_history(15)
        if not events:
            self._note(self.journal_box, tr("Журнал пуст."))
        for e in events:
            line = ctk.CTkFrame(self.journal_box, fg_color="transparent")
            line.pack(fill="x", pady=3)
            ctk.CTkLabel(line, text=e["time"], width=130, anchor="w", text_color=MUTED, font=font(12)
                         ).pack(side="left")
            pill(line, *JOURNAL.get(e["action"], JOURNAL["off"])).pack(side="left")
            name = loc(self.tweaks[e["id"]], "name") if e["id"] in self.tweaks else e["name"]
            ctk.CTkLabel(line, text=name, anchor="w", text_color=TEXT, font=font(12)
                         ).pack(side="left", padx=(12, 0))

    def _render_rp(self, items, err):
        self._clear(self.rp_box)
        if items is None and err is None:
            self._note(self.rp_box, tr("Загружаю список…"))
        elif err:
            self._note(self.rp_box, tr("Не удалось получить список точек: {err}").format(err=err), ORANGE)
        elif not items:
            self._note(self.rp_box, tr("Точек восстановления нет. Возможно, защита системы выключена для диска C:."))
        else:
            for p in items[:8]:
                line = ctk.CTkFrame(self.rp_box, fg_color=CARD_ALT, corner_radius=10)
                line.pack(fill="x", pady=3)
                ctk.CTkLabel(line, text=p["t"], width=140, anchor="w", text_color=MUTED, font=font(12)
                             ).pack(side="left", padx=(14, 0), pady=9)
                ctk.CTkLabel(line, text=p["d"], anchor="w", text_color=TEXT, font=font(12)
                             ).pack(side="left", padx=(6, 14))

    # ---------- действия ----------

    def _revert_one(self, tid):
        self.rows[tid].set_busy(tr("откатываю…"))
        self._status(tr("Откатываю…"))
        self.jobs.put(("toggle", tid, False, False))

    def _revert_all(self):
        if not engine.get_applied(self.tweaks):
            self._status(tr("Откатывать нечего."))
            return
        if not messagebox.askyesno(APP_NAME, tr("Откатить все изменения, сделанные через WinOpti?"), parent=self):
            return
        self.revert_all_btn.configure(state="disabled")
        self._status(tr("Откатываю все изменения…"))
        self.jobs.put(("revert_all",))

    def _create_restore_point(self):
        self.rp_create_btn.configure(state="disabled")
        self._status(tr("Создаю точку восстановления…"))
        self.jobs.put(("restore_point",))

    def _open_restore(self):
        try:
            engine.open_system_restore()
        except Exception as e:
            self._status(tr("Не удалось открыть восстановление системы: {err}").format(err=e), True)

    # ---------- фоновый поток ----------

    def _job_revert_all(self):
        done, errors = 0, []
        for it in engine.get_applied(self.tweaks):
            t = self.tweaks[it["id"]]
            try:
                warning = engine.revert_tweak(t)
                if warning:
                    errors.append(f"{loc(t, 'name')}: {warning}")
                done += 1
            except Exception as e:
                errors.append(f"{loc(t, 'name')}: {e}")
            state, _ = self._read_state(t)
            self.results.put(("state", t["id"], state, None))
        text = tr("Откачено изменений: {n}.").format(n=done)
        if errors:
            text += " " + tr("Ошибки: {list}").format(list="; ".join(errors))
        self.results.put(("batch_done", text, bool(errors)))

    def _job_restore_point(self):
        ok, msg = engine.create_restore_point_msg("WinOpti " + datetime.now().strftime("%Y-%m-%d %H:%M"))
        text = tr("Точка восстановления создана.") if ok else tr("Точка не создана: {msg}").format(msg=msg[:160])
        self.results.put(("message", text, not ok))
        self.results.put(("rp_done",))
        self._job_rp_list()

    def _job_rp_list(self):
        try:
            items, err = engine.list_restore_points(), None
        except Exception as e:
            items, err = [], str(e)
        self.results.put(("rp_list", items, err))

    # ---------- ответы ----------

    def _on_rp_list(self, items, err):
        self._render_rp(items, err)

    def _on_rp_done(self):
        self.rp_create_btn.configure(state="normal")
