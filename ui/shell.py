"""winopt/ui/shell.py: каркас окна. Меню слева, шапка страницы, уведомления, анимации,
переключение вкладок и общие помощники для страниц (_card, _note, _scroll, _clear)."""
import ctypes
import os
import subprocess
import sys
import time
import tkinter

import customtkinter as ctk

import engine
import i18n
from ui.theme import *  # noqa: F403 — палитра, шрифты, иконки, кнопки, tr

GWL_EXSTYLE, WS_EX_LAYERED = -20, 0x80000
GCL_STYLE, CS_REDRAW = -26, 0x3  # CS_VREDRAW | CS_HREDRAW
RDW_INVALIDATE, RDW_ERASE, RDW_ALLCHILDREN, RDW_FRAME = 0x1, 0x4, 0x80, 0x400


def clear(box):
    for w in box.winfo_children():
        w.destroy()


class ShellMixin:
    """Требует от App: self.nav, self.pages, self._builders, self.titles/subtitles/icons, self.cats, self.groups,
    self.states."""

    # ---------- анимации ----------

    def _animate(self, key, ms, step, done=None):
        """step(t) с t от 0 до 1 (уже сглаженным), ~60 кадров в секунду через after().
        Новая анимация с тем же key отменяет предыдущую, чтобы быстрые клики не дёргали интерфейс."""
        old = self._anims.pop(key, None)
        if old:
            self.after_cancel(old)
        start = time.perf_counter()

        def tick():
            t = min((time.perf_counter() - start) * 1000 / ms, 1.0)
            try:
                step(ease(t))
            except Exception:  # виджет успели уничтожить
                self._anims.pop(key, None)
                return
            if t < 1:
                self._anims[key] = self.after(16, tick)
            else:
                self._anims.pop(key, None)
                if done:
                    done()
        tick()

    def _build_in_chunks(self, key, items, make, first=3, chunk=2, done=None):
        """Создать карточки порциями: первые сразу, остальные следующими тиками after(). Страница появляется
        мгновенно, а окно не замирает на секунду, пока customtkinter рисует десятки карточек.
        Новый вызов с тем же key отменяет недостроенный предыдущий (например, при наборе в поиске)."""
        old = self._chunk_jobs.pop(key, None)
        if old:
            self.after_cancel(old)
        items = list(items)

        def step(start, n):
            if time.perf_counter() < self._moving_until:  # окно двигают: не отнимаем у него кадры
                self._chunk_jobs[key] = self.after(60, lambda: step(start, n))
                return
            for it in items[start:start + n]:
                make(it)
            nxt = start + n
            if nxt < len(items):
                self._chunk_jobs[key] = self.after(1, lambda: step(nxt, chunk))
            else:
                self._chunk_jobs.pop(key, None)
                if done:
                    done()
        step(0, first)

    def _set_alpha(self, t):
        try:
            self.attributes("-alpha", t)
        except Exception:
            pass

    def _unlayer(self):
        """После плавного появления снять с окна WS_EX_LAYERED. Tk включает его ради прозрачности и не
        выключает даже при alpha = 1, а layered-окно Windows перетаскивает и перерисовывает заметно медленнее."""
        try:
            u = ctypes.windll.user32
            u.GetWindowLongW.restype = ctypes.c_long
            hwnd = u.GetParent(self.winfo_id())  # внешнее окно с рамкой, а не внутренняя область Tk
            ex = u.GetWindowLongW(hwnd, GWL_EXSTYLE)
            if ex & WS_EX_LAYERED:
                u.SetWindowLongW(hwnd, GWL_EXSTYLE, ex & ~WS_EX_LAYERED)
                u.RedrawWindow(hwnd, None, None, RDW_ERASE | RDW_INVALIDATE | RDW_FRAME | RDW_ALLCHILDREN)
        except Exception:
            pass

    def _no_full_redraw(self):
        """Снять CS_HREDRAW/CS_VREDRAW с классов окон Tk (TkTopLevel и TkChild — общие для всех виджетов).
        С ними Windows при каждом шаге растягивания перерисовывает все ~450 дочерних окон customtkinter
        (~300 мс на шаг); виджеты Tk и так перерисовывают себя сами, когда меняется их размер."""
        try:
            u = ctypes.windll.user32
            u.GetClassLongPtrW.restype = u.SetClassLongPtrW.restype = ctypes.c_ssize_t
            u.SetClassLongPtrW.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_ssize_t]
            for hwnd in (u.GetParent(self.winfo_id()), self.winfo_id()):
                style = u.GetClassLongPtrW(hwnd, GCL_STYLE)
                if style & CS_REDRAW:
                    u.SetClassLongPtrW(hwnd, GCL_STYLE, style & ~CS_REDRAW)
        except Exception:
            pass

    # ---------- меню ----------

    def _build_sidebar(self, groups):
        side = ctk.CTkFrame(self, width=268, corner_radius=0, fg_color=SIDEBAR)
        side.grid(row=0, column=0, sticky="nsew")
        side.pack_propagate(False)

        # низ панели (пакуем первым, чтобы прижать вниз)
        foot = ctk.CTkFrame(side, fg_color="transparent")
        foot.pack(side="bottom", fill="x", padx=18, pady=(10, 14))
        self.restore_var = ctk.BooleanVar(value=True)
        ctk.CTkCheckBox(foot, text=tr("Точка восстановления"), variable=self.restore_var, font=font(12),
                        text_color=MUTED, fg_color=ACCENT, hover_color=ACCENT_HOVER, border_color="#3a4358",
                        checkbox_width=18, checkbox_height=18, corner_radius=5).pack(anchor="w")
        status = ctk.CTkFrame(foot, fg_color="transparent")
        status.pack(fill="x", pady=(8, 0))
        ctk.CTkLabel(status, text=tr("●  администратор") if self.admin else tr("●  нет прав администратора"),
                     text_color=GREEN if self.admin else RED, font=font(11), height=18).pack(side="left")
        lang = ctk.CTkSegmentedButton(status, values=["RU", "EN"], command=self._switch_lang, height=22, width=76,
                                      font=font(11, True), fg_color="#1f2533", selected_color=ACCENT,
                                      selected_hover_color=ACCENT_HOVER, unselected_color="#1f2533",
                                      unselected_hover_color="#2d3548", text_color=TEXT)
        lang.set(i18n.LANG.upper())
        lang.pack(side="right")
        self.lang_switch = lang
        ctk.CTkFrame(side, height=1, fg_color=BORDER).pack(side="bottom", fill="x", padx=18)

        # логотип
        logo = ctk.CTkFrame(side, fg_color="transparent")
        logo.pack(anchor="w", padx=18, pady=(20, 12))
        ctk.CTkLabel(logo, text="", image=logo_image(32)).grid(row=0, column=0, rowspan=2, padx=(0, 10))
        name = ctk.CTkFrame(logo, fg_color="transparent")
        name.grid(row=0, column=1, sticky="sw")
        ctk.CTkLabel(name, text="Win", font=font(20, True), text_color=TEXT, height=24).pack(side="left")
        ctk.CTkLabel(name, text="Opti", font=font(20, True), text_color=ACCENT, height=24).pack(side="left")
        ctk.CTkLabel(logo, text=f"Windows {engine.windows_version()}", text_color=DIM, font=font(11), height=14
                     ).grid(row=1, column=1, sticky="nw")
        self._build_search_entry(side)

        # вкладки: прокручиваются, если окно низкое, поэтому никогда не обрезаются
        nav = ctk.CTkScrollableFrame(side, fg_color="transparent", corner_radius=0,
                                     scrollbar_button_color=SIDEBAR, scrollbar_button_hover_color="#2a3040")
        nav.pack(fill="both", expand=True, padx=(6, 0))
        for i, (label, cats) in enumerate(groups):
            self._nav_label(nav, label, first=i == 0)
            for cat in cats:
                self._nav_item(nav, cat)
        # полоска выбранной вкладки: одна на всё меню, плавно переезжает к новому пункту
        self.nav_indicator = ctk.CTkFrame(nav, width=3, height=18, corner_radius=2, fg_color=ACCENT)

    def _switch_lang(self, value):
        """Язык меняется перезапуском окна: так все 60+ карточек гарантированно перерисуются.
        Новый процесс наследует права администратора, повторного запроса UAC нет."""
        lang = value.lower()
        if lang == i18n.LANG:
            return
        if self._working or not self.jobs.empty() or self.clean_busy or self.preset_busy:
            self.lang_switch.set(i18n.LANG.upper())
            self._status(tr("Подожди, пока закончится текущая операция, и переключи язык ещё раз."), True)
            return
        i18n.set_lang(lang)
        if getattr(sys, "frozen", False):
            cmd = [sys.executable]
        else:
            exe = os.path.join(os.path.dirname(sys.executable), "pythonw.exe")
            script = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "gui.py")
            cmd = [exe if os.path.exists(exe) else sys.executable, script]
        subprocess.Popen(cmd)
        self._animate("alpha", 180, lambda t: self._set_alpha(1 - t), self.destroy)

    def _nav_label(self, side, text, first=False):
        ctk.CTkLabel(side, text=text, font=font(10, True), text_color=DIM, anchor="w", height=16
                     ).pack(fill="x", padx=12, pady=(2 if first else 12, 3))

    def _nav_item(self, side, cat):
        wrap = ctk.CTkFrame(side, fg_color=SIDEBAR, corner_radius=9, height=36)
        wrap.pack(fill="x", padx=(2, 8), pady=1)
        wrap.pack_propagate(False)
        badge = ctk.CTkLabel(wrap, text="", text_color=MUTED, font=font(11, True), height=20, corner_radius=8,
                             fg_color="transparent")
        badge.pack(side="right", padx=(0, 8))
        btn = ctk.CTkButton(wrap, text=f" {self.titles[cat]}", image=icon(self.icons[cat], MUTED, 18),
                            compound="left", anchor="w", fg_color="transparent", hover_color=HOVER,
                            text_color=MUTED, font=font(13), corner_radius=9, height=36,
                            command=lambda c=cat: self._show(c))
        btn.pack(side="left", fill="both", expand=True, padx=(6, 0))
        self.nav[cat] = {"wrap": wrap, "btn": btn, "badge": badge, "icon": self.icons[cat]}

    def _badge(self, cat, text):
        """Счётчик у пункта меню: маленькая «таблетка», пустая — невидима."""
        if self.nav[cat].get("badge_text") == text:
            return  # перерисовка CTkLabel стоит ~2 мс, а счётчиков 8 — после каждого переключения
        self.nav[cat]["badge_text"] = text
        self.nav[cat]["badge"].configure(text=f" {text} " if text else "",
                                         fg_color="#1f2533" if text else "transparent")

    def _move_indicator(self, cat):
        wrap = self.nav[cat]["wrap"]
        if wrap.winfo_height() <= 1:  # меню ещё не разложено
            self.after(60, lambda: self._move_indicator(cat))
            return
        x = wrap.winfo_x() + 2
        y_to = wrap.winfo_y() + (wrap.winfo_height() - 18) / 2
        y_from = y_to if self._indicator_y is None else self._indicator_y
        self.nav_indicator.lift()

        def step(t):
            self._indicator_y = y_from + (y_to - y_from) * t
            self.nav_indicator.place(x=x, y=self._indicator_y)
        self._animate("indicator", 260, step)

    def _style_nav(self, cat, selected):
        item = self.nav[cat]
        item["btn"].configure(text_color=TEXT if selected else MUTED,
                              image=icon(item["icon"], ACCENT if selected else MUTED, 18))
        # без плавного перекрашивания: CTkFrame перекрашивает и всех детей, ~11 мс на кадр — анимация
        # тормозила переключение вкладок; движение даёт выезжающий индикатор
        item["wrap"].configure(fg_color=SELECTED if selected else SIDEBAR)

    # ---------- шапка и область страниц ----------

    def _build_header(self, main):
        header = ctk.CTkFrame(main, fg_color="transparent")
        header.pack(fill="x", padx=28, pady=(20, 0))
        header.columnconfigure(0, weight=1)
        self.title_lbl = ctk.CTkLabel(header, text="", anchor="w", text_color=TEXT, font=font(24, True), height=32)
        self.title_lbl.grid(row=0, column=0, sticky="w")
        self.subtitle_lbl = ctk.CTkLabel(header, text="", anchor="w", text_color=MUTED, font=font(12), height=18)
        self.subtitle_lbl.grid(row=1, column=0, sticky="w")
        self.refresh_btn = button(header, tr("Обновить"), "secondary", self._refresh, width=100, height=32)
        self.refresh_btn.grid(row=0, column=1, rowspan=2, sticky="e")

    def _banner(self, main, text, fg, bg):
        autowrap(ctk.CTkLabel(main, text=text, text_color=fg, fg_color=bg, corner_radius=10, wraplength=700,
                              justify="left", anchor="w", font=font(12)), main, 90
                 ).pack(fill="x", padx=28, pady=(10, 0), ipady=6, ipadx=12)

    def _page(self, cat):
        """Страница вкладки; строится при первом обращении."""
        if cat not in self.pages:
            self.pages[cat] = self._builders[cat]()
        return self.pages[cat]

    def _show(self, cat):
        if cat == self.current:
            return
        page = self._page(cat)
        if self.current in self.nav:
            self._style_nav(self.current, False)
        prev_cat, prev = self.current, self.pages.get(self.current)
        self.current = cat
        # новая страница ложится поверх прежней, а прежняя убирается из окна: скрытые, но разложенные
        # страницы — это тысячи окон Windows, с ними тормозит перетаскивание окна
        tkinter.Place.place_configure(page, x=0, y=0, relwidth=1, relheight=1)
        page.tkraise()
        if prev is not None and prev is not page:
            prev.place_forget()
            on_hide = getattr(self, f"_on_hide_{prev_cat}", None)  # вкладка может освободить лишние окна
            if on_hide:
                on_hide()
        for scroll in [s for s in self._fit_pending if self._on_current(s)]:
            self._fit_pending.discard(scroll)  # окно растягивали, пока страница была скрыта
            self._fit_scroll(scroll)
        if cat in self.nav:
            self._style_nav(cat, True)
            self._move_indicator(cat)
        else:  # страница без пункта меню (поиск)
            self.nav_indicator.place_forget()
        # заголовок проявляется и чуть выезжает слева
        self.title_lbl.configure(text=self.titles[cat], text_color=BG)
        self.subtitle_lbl.configure(text=self.subtitles[cat], text_color=BG)

        def title_step(t):
            self.title_lbl.configure(text_color=mix(BG, TEXT, t))
            self.subtitle_lbl.configure(text_color=mix(BG, MUTED, t))
            shift = round(16 * (1 - t))
            self.title_lbl.grid_configure(padx=(shift, 0))
            self.subtitle_lbl.grid_configure(padx=(shift, 0))
        self._animate("title", 300, title_step)
        on_show = getattr(self, f"_on_show_{cat}", None)  # вкладке нужно что-то загрузить при открытии
        if on_show:
            on_show()
        self._update_header()

    def _progress_row(self, page, cat):
        """Строка «N из M включено» с полосой — внутри вкладки с твиками, а не в шапке: иначе шапка меняла бы
        высоту при переходе на другие вкладки и сдвигала (перерисовывала) всю страницу."""
        box = ctk.CTkFrame(page, fg_color="transparent", height=24)
        box.pack(fill="x", padx=(0, 26), pady=(0, 8))
        label = ctk.CTkLabel(box, text="", text_color=MUTED, font=font(12, True), height=24)
        label.pack(side="left")
        bar = ctk.CTkProgressBar(box, height=6, corner_radius=3, fg_color="#232938", progress_color=ACCENT)
        bar.pack(side="left", fill="x", expand=True, padx=(16, 0))
        bar.set(0)
        self.progress_rows[cat] = {"label": label, "bar": bar, "on": 0}

    def _update_header(self):
        row = self.progress_rows.get(self.current)  # полоса прогресса только у вкладок с твиками
        if row is None:
            return
        if not self.loaded:
            row["label"].configure(text=tr("Читаю состояние…"))
            row["bar"].set(0)
            return
        on, total = self._cat_count(self.current)
        p_from, p_to = row["bar"].get(), on / total if total else 0
        n_from = row["on"]
        template = tr("{on} из {total} включено")

        def step(t):  # полоса и число плавно доезжают до нового значения
            row["bar"].set(p_from + (p_to - p_from) * t)
            row["on"] = round(n_from + (on - n_from) * t)
            row["label"].configure(text=template.format(on=row["on"], total=total))
        self._animate("progress", 450, step)

    def _cat_count(self, cat):
        """(сколько включено, сколько всего) твиков вкладки — по состояниям, без карточек."""
        ids = [t["id"] for t in self.groups[cat]]
        return sum(1 for tid in ids if self.states[tid] == "on"), len(ids)

    def _update_counts(self):
        """Счётчики на вкладках: сколько твиков включено, и сколько изменений можно откатить."""
        if self.loaded:
            for cat in self.cats:
                self._badge(cat, "{}/{}".format(*self._cat_count(cat)))
        applied = len(engine.get_applied(self.tweaks))
        self._badge("restore", str(applied) if applied else "")
        self._render_presets()
        self._update_header()

    # ---------- уведомления ----------

    def _build_toast(self, main):
        """Уведомление в правом нижнем углу: выезжает снизу и само прячется."""
        self.toast = ctk.CTkFrame(main, corner_radius=12, fg_color=CARD_ALT, border_width=1, border_color=BORDER)
        self.toast_strip = ctk.CTkFrame(self.toast, width=4, height=8, corner_radius=2, fg_color=ACCENT)
        self.toast_strip.pack(side="left", fill="y", padx=(10, 0), pady=10)
        self.toast_lbl = ctk.CTkLabel(self.toast, text="", anchor="w", justify="left", wraplength=420,
                                      text_color=TEXT, font=font(12))
        self.toast_lbl.pack(side="left", padx=(10, 16), pady=10)
        for w in (self.toast, self.toast_lbl):
            w.bind("<Button-1>", lambda _e: self._toast_hide())  # клик — закрыть

    def _toast_place(self, y):
        self.toast.place(relx=1, rely=1, x=-22, y=y, anchor="se")

    def _toast_hide(self):
        if self._toast_job:
            self.after_cancel(self._toast_job)
            self._toast_job = None
        if not self.toast.winfo_ismapped():
            return
        h = self.toast.winfo_height() + 30
        self._animate("toast", 220, lambda t: self._toast_place(-18 + (h + 18) * t), self.toast.place_forget)

    def _status(self, text, error=False):
        """Уведомление: выезжает снизу справа, прячется само (ошибки висят дольше), клик — закрыть."""
        if not text:
            return
        self.toast_strip.configure(fg_color=RED if error else ACCENT)
        self.toast_lbl.configure(text=text, text_color=TEXT)
        if self._toast_job:
            self.after_cancel(self._toast_job)
        self.toast.lift()
        if self.toast.winfo_ismapped() and "toast" not in self._anims:
            self._toast_place(-18)  # уже на экране: просто новый текст
        else:
            self.toast.update_idletasks()
            h = self.toast.winfo_reqheight() + 30
            self._animate("toast", 280, lambda t: self._toast_place(h - (h + 18) * t))
        self._toast_job = self.after(9000 if error else 4500, self._toast_hide)

    # ---------- помощники для страниц ----------

    def _scroll(self, page):
        scroll = ctk.CTkScrollableFrame(page, fg_color="transparent", scrollbar_button_color="#2a3040",
                                        scrollbar_button_hover_color="#3a4358")
        scroll.pack(fill="both", expand=True)
        # пока окно растягивают, содержимое держит прежнюю ширину и подстраивается один раз, когда
        # растягивание закончилось: иначе каждый шаг мыши перекладывал бы все карточки страницы
        scroll._parent_canvas.bind("<Configure>", lambda _e: self._fit_scroll(scroll))
        return scroll

    def _fit_scroll(self, scroll):
        job = self._fit_jobs.pop(scroll, None)
        if job:
            self.after_cancel(job)
        if not self._on_current(scroll):  # скрытые страницы подгоним, когда их откроют
            self._fit_pending.add(scroll)
            return
        wait = self._resizing_until - time.perf_counter()
        if wait > 0:
            self._fit_jobs[scroll] = self.after(int(wait * 1000) + 20, lambda: self._fit_scroll(scroll))
            return
        try:
            scroll._fit_frame_dimensions_to_canvas(None)
        except Exception:
            pass  # страницу успели уничтожить

    def _on_current(self, widget):
        page = self.pages.get(self.current)
        return page is not None and str(widget).startswith(str(page) + ".")

    def _on_root_configure(self, event):
        """Запомнить, что окно сейчас двигают или растягивают (событие приходит и от детей — их пропускаем)."""
        if event.widget is not self:
            return
        geometry = (event.x, event.y, event.width, event.height)
        if self._root_geometry is not None and geometry != self._root_geometry:
            self._moving_until = time.perf_counter() + 0.25
            if geometry[2:] != self._root_geometry[2:]:
                self._resizing_until = time.perf_counter() + 0.18
        self._root_geometry = geometry

    @staticmethod
    def _card(parent, title, text=None):
        card = ctk.CTkFrame(parent, corner_radius=12, fg_color=CARD, border_width=1, border_color=BORDER)
        card.pack(fill="x", padx=(0, 10), pady=5)
        ctk.CTkLabel(card, text=title, anchor="w", text_color=TEXT, font=font(15, True)
                     ).pack(fill="x", padx=18, pady=(14, 0))
        if text:
            autowrap(ctk.CTkLabel(card, text=text, anchor="w", justify="left", wraplength=680,
                                  text_color=MUTED, font=font(12)), card, 44).pack(fill="x", padx=18, pady=(2, 0))
        return card

    _clear = staticmethod(clear)

    def _note(self, box, text, color=MUTED):
        # заметки пересоздаются при каждой перерисовке, поэтому без autowrap (не копим обработчики):
        # ширину переноса берём по текущей ширине контейнера
        width = box.winfo_width()
        wrap = max(300, int(box._reverse_widget_scaling(width)) - 20) if width > 1 else 640
        ctk.CTkLabel(box, text=text, anchor="w", justify="left", wraplength=wrap,
                     text_color=color, font=font(12)).pack(fill="x", pady=4)

    # ---------- общие ответы фонового потока ----------

    def _on_message(self, text, error):
        self._status(text, error)
