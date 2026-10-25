"""winopt/ui/restore_page.py: вкладка «Восстановление» — откат изменений WinOpti, точки восстановления
Windows и журнал."""
from datetime import datetime
import tkinter
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
        protect = ctk.CTkFrame(card, fg_color="transparent")
        protect.pack(fill="x", padx=20, pady=(10, 0))
        self.rp_protect_lbl = ctk.CTkLabel(protect, text="", anchor="w", font=font(12))
        self.rp_protect_lbl.pack(side="left")
        self.rp_protect_btn = button(protect, tr("Включить защиту системы"), "primary", self._enable_protection,
                                     height=30)
        # точки копятся и занимают место на диске — их можно удалить разом
        self.rp_delete_btn = button(protect, tr("Удалить все точки"), "danger", self._delete_restore_points,
                                    width=150, height=30)
        self.rp_delete_btn.pack(side="right")
        self.rp_box = ctk.CTkFrame(card, fg_color="transparent")
        self.rp_box.pack(fill="x", padx=20, pady=(8, 16))

        # 3. журнал
        card = self._card(scroll, tr("Журнал"))
        self.journal_box = ctk.CTkFrame(card, fg_color="transparent")
        self.journal_box.pack(fill="x", padx=20, pady=(8, 16))
        return page

    def _on_show_restore(self):
        self._render_history()
        if self._rp_last == (None, None) and not self._rp_loading:
            self._refresh_restore()  # список точек — только когда вкладку открыли
        self._render_rp(*self._rp_last)

    def _on_drop_restore(self):
        self._history_sig = self._rp_drawn = None  # новая страница нарисует всё заново

    def _refresh_restore(self):
        self._history_sig = None
        self._render_history()
        if not self._rp_loading:
            self._rp_loading = True
            self.jobs.put(("rp_list",))

    def _render_history(self):
        # перерисовываем только видимую вкладку: иначе каждый переключённый твик пересоздавал бы десятки
        # строк, которых никто не видит; при открытии вкладки _on_show_restore нарисует свежее
        if self.current != "restore":
            return
        items = engine.get_applied(self.tweaks)
        events = engine.get_history(15)
        # ничего не изменилось с прошлого раза — не пересоздаём ~40 строк (это почти секунда)
        sig = (repr(items), repr(events), self.admin)
        if sig == self._history_sig:
            return
        self._history_sig = sig

        # применённые изменения
        self._clear(self.applied_box)
        if not items:
            self._note(self.applied_box, tr("Пока ничего не применено через WinOpti."))
        self._build_in_chunks("restore_applied", items, self._applied_row)
        self.revert_all_btn.configure(state="normal" if items and self.admin else "disabled")

        # журнал: имена твиков показываем на текущем языке, остальные записи — как записаны
        self._clear(self.journal_box)
        if not events:
            self._note(self.journal_box, tr("Журнал пуст."))
        self._build_in_chunks("restore_journal", events, self._journal_line, first=4, chunk=4)

    def _applied_row(self, it):
        """Строка применённого твика с кнопкой «Откатить»."""
        row = ctk.CTkFrame(self.applied_box, fg_color=CARD_ALT, corner_radius=10)
        row.pack(fill="x", pady=4)
        row.columnconfigure(0, weight=1)
        text(row, loc(self.tweaks[it["id"]], "name"), 13, True, bg=CARD_ALT
             ).grid(row=0, column=0, sticky="w", padx=16, pady=(10, 0))
        when = tr("применено {time}").format(time=it["time"]) if it["time"] else tr("применено")
        text(row, when, 11, color=MUTED, bg=CARD_ALT).grid(row=1, column=0, sticky="w", padx=16, pady=(3, 10))
        btn = button(row, tr("Откатить"), "secondary", lambda tid=it["id"]: self._revert_one(tid),
                     width=100, height=32)
        btn.grid(row=0, column=1, rowspan=2, padx=14)
        if not self.admin:
            btn.configure(state="disabled")

    def _journal_line(self, e):
        # строк до 15 — простыми надписями Tk (у CTkLabel три окна Windows, см. theme.text)
        line = tkinter.Frame(self.journal_box, bg=CARD)
        line.pack(fill="x", pady=4)
        scale = ctk.ScalingTracker.get_widget_scaling(line)
        line.columnconfigure(0, minsize=round(130 * scale))
        line.columnconfigure(1, minsize=round(140 * scale))
        label, _bg, color = JOURNAL.get(e["action"], JOURNAL["off"])
        name = loc(self.tweaks[e["id"]], "name") if e["id"] in self.tweaks else e["name"]
        text(line, e["time"], 12, color=MUTED).grid(row=0, column=0, sticky="w")
        text(line, label, 11, True, color).grid(row=0, column=1, sticky="w")
        text(line, name, 12).grid(row=0, column=2, sticky="w")

    def _render_protection(self):
        """Без включённой защиты системы Windows не создаёт точки вообще — показываем это явно."""
        if engine.restore_protection_enabled():
            self.rp_protect_lbl.configure(text=tr("●  Защита системы включена"), text_color=GREEN)
            self.rp_protect_btn.pack_forget()
        else:
            self.rp_protect_lbl.configure(text=tr("●  Защита системы выключена: точки восстановления не создаются"),
                                          text_color=RED)
            self.rp_protect_btn.pack(side="left", padx=(12, 0))
            self.rp_protect_btn.configure(state="normal" if self.admin else "disabled")

    def _render_rp(self, items, err):
        self._rp_last = (items, err)
        if "restore" not in self.pages:
            return
        self._render_protection()
        self._rp_delete_state()
        if self._rp_drawn == repr((items, err)):
            return
        self._rp_drawn = repr((items, err))
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
        self._set_busy(tid, tr("откатываю…"))
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

    def _enable_protection(self):
        self.rp_protect_btn.configure(state="disabled")
        self._status(tr("Включаю защиту системы…"))
        self.jobs.put(("enable_protection",))

    def _rp_delete_state(self):
        if self.current == "restore":
            can = self.admin and bool(self._rp_last[0]) and not self._rp_deleting
            self.rp_delete_btn.configure(state="normal" if can else "disabled")

    def _delete_restore_points(self):
        items = self._rp_last[0] or []
        if not items:
            self._status(tr("Точек восстановления нет — удалять нечего."))
            return
        text = tr("Удалить все точки восстановления Windows ({n} шт.)?\n\nВернуть систему к ним будет нельзя. "
                  "Изменения WinOpti это не затронет: их по-прежнему можно откатить кнопками выше.")
        if not messagebox.askyesno(APP_NAME, text.format(n=len(items)), icon="warning", parent=self):
            return
        self._rp_deleting = True
        self._rp_delete_state()
        self._status(tr("Удаляю точки восстановления…"))
        self.jobs.put(("rp_delete",))

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

    def _job_enable_protection(self):
        try:
            engine.enable_restore_protection()
            self.results.put(("message", tr("Защита системы включена."), False))
        except Exception as e:
            self.results.put(("message", tr("Не удалось включить защиту системы: {err}").format(err=e), True))
        self._job_rp_list()

    def _job_rp_delete(self):
        deleted, errors = engine.delete_restore_points()
        text = tr("Удалено точек восстановления: {n}.").format(n=deleted)
        if errors:
            text += " " + tr("Ошибки: {list}").format(list="; ".join(errors[:3]))
        self.results.put(("message", text, bool(errors)))
        self.results.put(("rp_deleted",))
        self._job_rp_list()

    def _fail_rp_delete(self, _error):
        self.results.put(("rp_deleted",))

    def _job_rp_list(self):
        try:
            items, err = engine.list_restore_points(), None
        except Exception as e:
            items, err = [], str(e)
        self.results.put(("rp_list", items, err))

    # ---------- ответы ----------

    def _on_rp_list(self, items, err):
        self._rp_loading = False
        self._render_rp(items, err)

    def _on_rp_deleted(self):
        self._rp_deleting = False
        self._rp_delete_state()

    def _on_rp_done(self):
        if "restore" in self.pages:
            self.rp_create_btn.configure(state="normal")
