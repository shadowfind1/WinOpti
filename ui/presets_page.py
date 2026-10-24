"""winopt/ui/presets_page.py: вкладка «Пресеты» — наборы твиков, включаемые одной кнопкой."""
from tkinter import messagebox

import customtkinter as ctk

import engine
from i18n import loc
from ui.theme import *  # noqa: F403


class PresetsMixin:

    def _build_presets_page(self, box):
        page = ctk.CTkFrame(box, fg_color="transparent")
        scroll = self._scroll(page)
        if not self.presets:
            self._note(scroll, tr("Файл presets.json не найден или пуст."))
        for p in self.presets:
            card = ctk.CTkFrame(scroll, corner_radius=14, fg_color=CARD, border_width=1, border_color=BORDER)
            card.pack(fill="x", padx=(0, 10), pady=6)
            card.columnconfigure(0, weight=1)
            ctk.CTkLabel(card, text=loc(p, "name"), anchor="w", text_color=TEXT, font=font(17, True)
                         ).grid(row=0, column=0, sticky="w", padx=20, pady=(16, 0))
            counter = pill(card, "", PILL_GRAY, MUTED)
            counter.grid(row=0, column=1, sticky="e", padx=20, pady=(16, 0))
            autowrap(ctk.CTkLabel(card, text=loc(p, "description"), anchor="w", justify="left", wraplength=640,
                                  text_color=MUTED, font=font(12)), card, 48
                     ).grid(row=1, column=0, columnspan=2, sticky="w", padx=20, pady=(4, 0))

            grid = ctk.CTkFrame(card, fg_color="transparent")
            grid.grid(row=2, column=0, columnspan=2, sticky="ew", padx=20, pady=(10, 0))
            grid.columnconfigure((0, 1), weight=1, uniform="col")
            items = {}
            for n, tid in enumerate(p["tweaks"]):
                lbl = ctk.CTkLabel(grid, text="", anchor="w", font=font(12))
                lbl.grid(row=n // 2, column=n % 2, sticky="w", pady=1)
                items[tid] = lbl

            bar = ctk.CTkFrame(card, fg_color="transparent")
            bar.grid(row=3, column=0, columnspan=2, sticky="w", padx=20, pady=(12, 16))
            apply_btn = button(bar, tr("Применить пресет"), "primary", lambda pid=p["id"]: self._preset_apply(pid))
            apply_btn.pack(side="left")
            revert_btn = button(bar, tr("Откатить"), "secondary", lambda pid=p["id"]: self._preset_revert(pid),
                                width=110)
            revert_btn.pack(side="left", padx=(10, 0))
            self.preset_ui[p["id"]] = {"items": items, "counter": counter,
                                       "apply": apply_btn, "revert": revert_btn}
        return page

    def _render_presets(self):
        """Состояние твиков в карточках пресетов и доступность кнопок."""
        applied = {it["id"] for it in engine.get_applied(self.tweaks)}
        for p in self.presets:
            ui = self.preset_ui[p["id"]]
            supported = on = 0
            for tid, lbl in ui["items"].items():
                row = self.rows[tid]
                name = loc(self.tweaks[tid], "name")
                if row.state == "n/a":
                    lbl.configure(text=f"○  {name}  ({tr('недоступно')})", text_color=DIM)
                    continue
                supported += 1
                if row.state == "on":
                    on += 1
                    lbl.configure(text=f"●  {name}", text_color=GREEN)
                elif row.state == "partial":
                    lbl.configure(text=f"◐  {name}", text_color=ORANGE)
                else:
                    lbl.configure(text=f"○  {name}", text_color=MUTED)
            if not self.loaded:
                ui["counter"].configure(text=f"  {tr('читаю состояние…')}  ", fg_color=PILL_GRAY, text_color=MUTED)
            elif on == supported:
                ui["counter"].configure(text="  " + tr("всё включено: {on} из {total}").format(on=on, total=supported)
                                        + "  ", fg_color=PILL_GREEN, text_color=GREEN)
            else:
                ui["counter"].configure(text="  " + tr("включено {on} из {total}").format(on=on, total=supported)
                                        + "  ", fg_color=PILL_GRAY, text_color=MUTED)
            idle = self.admin and self.loaded and not self.preset_busy
            ui["apply"].configure(state="normal" if idle and on < supported else "disabled")
            can_revert = idle and any(tid in applied for tid in p["tweaks"])
            ui["revert"].configure(state="normal" if can_revert else "disabled")

    # ---------- действия ----------

    def _preset(self, pid):
        return next(p for p in self.presets if p["id"] == pid)

    def _run_preset(self, p, ids, want_on):
        self.preset_busy = True
        for tid in ids:
            self.rows[tid].set_busy(tr("применяю…") if want_on else tr("откатываю…"))
        need_rp = self._need_restore_point(want_on)
        self._render_presets()
        template = tr("Пресет «{name}»: применяю…") if want_on else tr("Пресет «{name}»: откатываю…")
        self._status(template.format(name=loc(p, "name")))
        self.jobs.put(("preset", p["id"], ids, want_on, need_rp))

    def _preset_apply(self, pid):
        p = self._preset(pid)
        ids = [tid for tid in p["tweaks"]
               if self.rows[tid].can_edit and self.rows[tid].state in ("off", "partial")]
        if not ids:
            self._status(tr("Пресет «{name}»: всё уже включено.").format(name=loc(p, "name")))
            return
        lines = []
        for tid in ids:
            t = self.tweaks[tid]
            risk = f"  ({tr('средний риск')})" if t.get("risk") == "medium" else ""
            lines.append(f"•  {loc(t, 'name')}{risk}")
        text = tr("Пресет «{name}» включит:\n\n{items}\n\nВсё можно откатить на вкладке «Восстановление».")
        if not messagebox.askyesno(APP_NAME, text.format(name=loc(p, "name"), items="\n".join(lines)), parent=self):
            return
        self._run_preset(p, ids, True)

    def _preset_revert(self, pid):
        p = self._preset(pid)
        applied = {it["id"] for it in engine.get_applied(self.tweaks)}
        ids = [tid for tid in p["tweaks"] if tid in applied]
        if not ids:
            self._status(tr("Пресет «{name}»: откатывать нечего.").format(name=loc(p, "name")))
            return
        names = "\n".join(f"•  {loc(self.tweaks[tid], 'name')}" for tid in ids)
        text = tr("Откатить твики пресета «{name}», включённые через WinOpti?\n\n{items}\n\n"
                  "Они могут входить и в другие пресеты.")
        if not messagebox.askyesno(APP_NAME, text.format(name=loc(p, "name"), items=names), parent=self):
            return
        self._run_preset(p, ids, False)

    # ---------- фоновый поток ----------

    def _job_preset(self, pid, ids, want_on, need_rp):
        p = self._preset(pid)
        done, errors, after = 0, [], []
        if need_rp:
            ok, msg = engine.create_restore_point_msg("WinOpti")
            if not ok:
                errors.append(tr("точка восстановления не создана: {msg}").format(msg=msg[:140]))
        for tid in ids:
            t = self.tweaks[tid]
            try:
                warning = engine.apply_tweak(t) if want_on else engine.revert_tweak(t)
                done += 1
                if warning:
                    errors.append(f"{loc(t, 'name')}: {warning}")
                if loc(t, "after") and loc(t, "after") not in after:
                    after.append(loc(t, "after"))
            except Exception as e:
                errors.append(f"{loc(t, 'name')}: {e}")
            state, _ = self._read_state(t)
            self.results.put(("state", tid, state, None))
        template = tr("Пресет «{name}»: включено твиков: {done} из {total}.") if want_on else \
            tr("Пресет «{name}»: откачено твиков: {done} из {total}.")
        text = template.format(name=loc(p, "name"), done=done, total=len(ids))
        if after:
            text += " " + tr("Дальше: {what}.").format(what="; ".join(after))
        if errors:
            text += " " + tr("Внимание: {msg}.").format(msg="; ".join(errors))
        self.results.put(("batch_done", text, bool(errors)))
        self.results.put(("preset_done",))

    def _fail_preset(self, *_args):
        self.results.put(("preset_done",))

    # ---------- ответы ----------

    def _on_preset_done(self):
        self.preset_busy = False
        self._render_presets()
