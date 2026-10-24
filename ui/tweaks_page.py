"""winopt/ui/tweaks_page.py: вкладки с твиками (Приватность, Интерфейс, …): карточки и переключение."""
import customtkinter as ctk

import engine
from i18n import loc
from ui.theme import *  # noqa: F403


class TweakRow(ctk.CTkFrame):
    """Карточка одного твика: название, описание, риск, статус и переключатель."""

    STATUS = {
        "on": (tr("включено"), PILL_GREEN, GREEN),
        "off": (tr("выключено"), PILL_GRAY, MUTED),
        "partial": (tr("частично"), PILL_ORANGE, ORANGE),
        "error": (tr("не удалось прочитать"), PILL_RED, RED),
    }

    def __init__(self, master, tweak, can_edit, on_toggle):
        super().__init__(master, corner_radius=12, fg_color=CARD, border_width=1, border_color=BORDER)
        self.tweak = tweak
        self.on_toggle = on_toggle
        self.state = "loading"
        self.can_edit = can_edit and engine.is_supported(tweak)
        self.columnconfigure(0, weight=1)
        self._wrap = 560

        ctk.CTkLabel(self, text=loc(tweak, "name"), anchor="w", text_color=TEXT, font=font(14, True)
                     ).grid(row=0, column=0, sticky="w", padx=16, pady=(10, 0))
        self.desc = ctk.CTkLabel(self, text=loc(tweak, "description"), anchor="w", justify="left",
                                 wraplength=self._wrap, text_color=MUTED, font=font(12))
        self.desc.grid(row=1, column=0, sticky="w", padx=16, pady=(1, 0))

        meta = ctk.CTkFrame(self, fg_color="transparent")
        meta.grid(row=2, column=0, sticky="w", padx=16, pady=(6, 10))
        risk_text, risk_color = RISK.get(tweak.get("risk", "low"), RISK["low"])
        ctk.CTkLabel(meta, text=f"●  {risk_text}", text_color=risk_color, font=font(11), height=20).pack(side="left")
        self.status = pill(meta, "", PILL_GRAY, MUTED)
        self.status.pack(side="left", padx=(12, 0))

        self.switch = ctk.CTkSwitch(self, text="", width=50, command=self._clicked,
                                    progress_color=ACCENT, fg_color="#2b3243",
                                    button_color="#e8ebf2", button_hover_color="#ffffff")
        self.switch.grid(row=0, column=1, rowspan=3, padx=16)
        self.bind("<Configure>", self._on_resize)
        self.set_busy(tr("читаю состояние…"))

    def _on_resize(self, event):
        """Описание занимает всю ширину карточки: на широком окне карточки становятся ниже."""
        width = self._reverse_widget_scaling(event.width) if hasattr(self, "_reverse_widget_scaling") else event.width
        wrap = max(280, int(width) - 110)
        if abs(wrap - self._wrap) > 8:
            self._wrap = wrap
            self.desc.configure(wraplength=wrap)

    def _clicked(self):
        self.on_toggle(self, bool(self.switch.get()))

    def set_busy(self, text):
        self.switch.configure(state="disabled")
        self.status.configure(text=f"  {text}  ", fg_color=PILL_GRAY, text_color=MUTED)

    def set_state(self, state):
        """state: on / off / partial / n/a / error"""
        self.state = state
        if state == "on":
            self.switch.select()
        else:
            self.switch.deselect()
        if state == "n/a":
            text, bg, fg = tr("недоступно на Windows {v}").format(v=engine.windows_version()), PILL_GRAY, MUTED
        else:
            text, bg, fg = self.STATUS[state]
        self.status.configure(text=f"  {text}  ", fg_color=bg, text_color=fg)
        editable = self.can_edit and state in ("on", "off", "partial")
        self.switch.configure(state="normal" if editable else "disabled")


class TweaksMixin:

    def _build_tweak_page(self, box, cat):
        page = ctk.CTkFrame(box, fg_color="transparent")
        scroll = self._scroll(page)
        self.cat_rows[cat] = []
        for t in self.groups[cat]:
            row = TweakRow(scroll, t, self.admin, self._toggle)
            row.pack(fill="x", padx=(0, 10), pady=4)
            self.rows[t["id"]] = row
            self.cat_rows[cat].append(row)
        return page

    # ---------- действия ----------

    def _refresh(self):
        self.refresh_btn.configure(state="disabled")
        for row in self.rows.values():
            row.set_busy(tr("читаю состояние…"))
        self.jobs.put(("read_all",))
        self.jobs.put(("rp_list",))

    def _toggle(self, row, want_on):
        row.set_busy(tr("применяю…") if want_on else tr("откатываю…"))
        self.jobs.put(("toggle", row.tweak["id"], want_on, self._need_restore_point(want_on)))

    def _need_restore_point(self, want_on):
        """Точку восстановления создаём один раз за сеанс, перед первым включением."""
        need = want_on and self.restore_var.get() and not self.restore_done
        if need:
            self.restore_done = True
        return need

    # ---------- фоновый поток ----------

    def _read_state(self, t):
        if not engine.is_supported(t):
            return "n/a", None
        try:
            return engine.get_state(t), None
        except Exception as e:
            return "error", f"{loc(t, 'name')}: {e}"

    def _job_read_all(self):
        for tid, t in self.tweaks.items():
            state, err = self._read_state(t)
            self.results.put(("state", tid, state, err))
        self.results.put(("read_done",))

    def _job_toggle(self, tid, want_on, need_rp):
        t = self.tweaks[tid]
        name = loc(t, "name")
        note = ""
        try:
            if want_on:
                if need_rp:
                    ok, msg = engine.create_restore_point_msg("WinOpti")
                    if not ok:
                        note = " " + tr("Точка восстановления не создана: {msg}").format(msg=msg[:140])
                warning = engine.apply_tweak(t)
            else:
                warning = engine.revert_tweak(t)
            if warning:
                note += " " + tr("Внимание: {msg}.").format(msg=warning)
            state, _ = self._read_state(t)
            text = tr("{name}: готово.").format(name=name)
            if loc(t, "after"):
                text += f" {loc(t, 'after')}."
            self.results.put(("toggled", tid, state, text + note, False))
        except Exception as e:
            state, _ = self._read_state(t)
            self.results.put(("toggled", tid, state, tr("{name}: ошибка: {err}").format(name=name, err=e), True))

    # ---------- ответы ----------

    def _set_row_state(self, tid, state):
        """Основная карточка и её копия в результатах поиска, если она есть."""
        self.rows[tid].set_state(state)
        if tid in self.search_rows:
            self.search_rows[tid].set_state(state)

    def _on_state(self, tid, state, err):
        self._set_row_state(tid, state)
        if err:
            self._status(err, True)

    def _on_toggled(self, tid, state, text, is_err):
        self._set_row_state(tid, state)
        self._status(text, is_err)
        self._update_counts()
        self._render_history()

    def _on_batch_done(self, text, is_err):
        self._status(text, is_err)
        self._update_counts()
        self._render_history()

    def _on_read_done(self):
        self.loaded = True
        self.refresh_btn.configure(state="normal")
        self._update_counts()
