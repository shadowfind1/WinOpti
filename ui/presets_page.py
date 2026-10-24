"""winopt/ui/presets_page.py: вкладка «Пресеты» — наборы твиков, включаемые одной кнопкой,
и свои настройки: экспорт включённых твиков в файл и импорт на другом ПК."""
import json
from datetime import datetime
from pathlib import Path
import tkinter
from tkinter import filedialog, messagebox

import customtkinter as ctk

import engine
from i18n import loc
from ui.theme import *  # noqa: F403

EXPORT_FORMAT = 1


class PresetsMixin:

    def _build_presets_page(self, box):
        page = ctk.CTkFrame(box, fg_color="transparent")
        scroll = self._scroll(page)

        card = self._card(scroll, tr("Свои настройки"),
                          tr("Сохрани включённые твики в файл и примени их на другом ПК или после переустановки "
                             "Windows. Импорт только включает твики из файла и ничего не выключает."))
        bar = ctk.CTkFrame(card, fg_color="transparent")
        bar.pack(fill="x", padx=20, pady=(12, 16))
        button(bar, tr("Экспорт в файл"), "secondary", self._export_settings).pack(side="left")
        self.import_btn = button(bar, tr("Импорт из файла"), "secondary", self._import_settings)
        self.import_btn.pack(side="left", padx=(10, 0))

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

            grid = tkinter.Frame(card, bg=CARD)  # список твиков — простыми надписями Tk (см. theme.text)
            grid.grid(row=2, column=0, columnspan=2, sticky="ew", padx=20, pady=(10, 0))
            grid.columnconfigure((0, 1), weight=1, uniform="col")
            items = {}
            for n, tid in enumerate(p["tweaks"]):
                lbl = text(grid, "", 12)
                lbl.grid(row=n // 2, column=n % 2, sticky="w", pady=4)
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

    def _on_show_presets(self):
        self._render_presets()

    def _render_presets(self):
        """Состояние твиков в карточках пресетов и доступность кнопок. Скрытую вкладку не трогаем
        (~30 мс на каждое переключение твика) — она обновится при открытии, см. _on_show_presets."""
        if self.current != "presets":
            return
        applied = {it["id"] for it in engine.get_applied(self.tweaks)}
        for p in self.presets:
            ui = self.preset_ui[p["id"]]
            supported = on = 0
            for tid, lbl in ui["items"].items():
                state = self.states[tid]
                name = loc(self.tweaks[tid], "name")
                if state == "n/a":
                    lbl.configure(text=f"○  {name}  ({tr('недоступно')})", fg=DIM)
                    continue
                supported += 1
                if state == "on":
                    on += 1
                    lbl.configure(text=f"●  {name}", fg=GREEN)
                elif state == "partial":
                    lbl.configure(text=f"◐  {name}", fg=ORANGE)
                else:
                    lbl.configure(text=f"○  {name}", fg=MUTED)
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
        idle = self.admin and self.loaded and not self.preset_busy
        self.import_btn.configure(state="normal" if idle else "disabled")

    # ---------- действия ----------

    def _preset(self, pid):
        return next(p for p in self.presets if p["id"] == pid)

    def _run_preset(self, name, ids, want_on):
        """Включить/откатить набор твиков: пресет или импорт из файла."""
        self.preset_busy = True
        for tid in ids:
            self._set_busy(tid, tr("применяю…") if want_on else tr("откатываю…"))
        need_rp = self._need_restore_point(want_on)
        self._render_presets()
        template = tr("Пресет «{name}»: применяю…") if want_on else tr("Пресет «{name}»: откатываю…")
        self._status(template.format(name=name))
        self.jobs.put(("preset", name, ids, want_on, need_rp))

    def _preset_apply(self, pid):
        p = self._preset(pid)
        ids = [tid for tid in p["tweaks"] if self._can_edit(tid) and self.states[tid] in ("off", "partial")]
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
        self._run_preset(loc(p, "name"), ids, True)

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
        self._run_preset(loc(p, "name"), ids, False)

    # ---------- свои настройки: экспорт и импорт ----------

    def _export_settings(self):
        if not self.loaded:
            self._status(tr("Подожди, пока прочитается состояние твиков."))
            return
        ids = [tid for tid, state in self.states.items() if state == "on"]
        path = filedialog.asksaveasfilename(
            parent=self, title=tr("Экспорт настроек"), defaultextension=".json",
            initialfile=f"winopti-{datetime.now():%Y-%m-%d}.json", filetypes=[("WinOpti", "*.json")])
        if not path:
            return
        data = {"app": APP_NAME, "format": EXPORT_FORMAT, "created": datetime.now().strftime("%Y-%m-%d %H:%M"),
                "windows": engine.windows_version(), "tweaks": ids}
        try:
            Path(path).write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        except OSError as e:
            self._status(tr("Не удалось сохранить файл: {err}").format(err=e), True)
            return
        self._status(tr("Сохранено твиков: {n}. Файл: {path}").format(n=len(ids), path=path))

    def _import_settings(self):
        path = filedialog.askopenfilename(parent=self, title=tr("Импорт настроек"),
                                          filetypes=[("WinOpti", "*.json"), ("*", "*.*")])
        if not path:
            return
        try:
            data = json.loads(Path(path).read_text(encoding="utf-8"))
            if data.get("app") != APP_NAME or not isinstance(data.get("tweaks"), list):
                raise ValueError(tr("это не файл настроек WinOpti"))
        except (OSError, ValueError) as e:
            self._status(tr("Не удалось прочитать файл: {err}").format(err=e), True)
            return
        known = [tid for tid in data["tweaks"] if tid in self.tweaks]
        unknown = len(data["tweaks"]) - len(known)
        na = [tid for tid in known if self.states[tid] == "n/a"]
        ids = [tid for tid in known if self._can_edit(tid) and self.states[tid] in ("off", "partial")]
        notes = []
        if na:
            notes.append(tr("недоступно на этой Windows: {n}").format(n=len(na)))
        if unknown:
            notes.append(tr("неизвестно этой версии WinOpti: {n}").format(n=unknown))
        if not ids:
            text = tr("Всё из файла уже включено.")
            if notes:
                text += " (" + "; ".join(notes) + ")"
            self._status(text)
            return
        lines = "\n".join(f"•  {loc(self.tweaks[tid], 'name')}" for tid in ids)
        text = tr("Включить из файла:\n\n{items}").format(items=lines)
        if notes:
            text += "\n\n" + tr("Пропущено: {list}.").format(list="; ".join(notes))
        text += "\n\n" + tr("Всё можно откатить на вкладке «Восстановление».")
        if not messagebox.askyesno(APP_NAME, text, parent=self):
            return
        self._run_preset(tr("импорт из файла"), ids, True)

    # ---------- фоновый поток ----------

    def _job_preset(self, name, ids, want_on, need_rp):
        done, errors, after = 0, [], []
        if need_rp:
            ok, msg = engine.auto_restore_point_msg("WinOpti")
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
        text = template.format(name=name, done=done, total=len(ids))
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
