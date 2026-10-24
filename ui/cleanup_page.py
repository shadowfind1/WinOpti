"""winopt/ui/cleanup_page.py: вкладка «Очистка» — временные файлы, кэш обновлений, корзина."""
from tkinter import messagebox

import customtkinter as ctk

import cleaner
from ui.theme import *  # noqa: F403


class CleanupMixin:

    def _build_cleanup_page(self, box):
        page = ctk.CTkFrame(box, fg_color="transparent")
        scroll = self._scroll(page)

        card = self._card(scroll, tr("Освободить место на диске"),
                          tr("WinOpti считает, сколько места занимает каждый пункт. Удаляется только то, "
                             "что Windows и программы создают заново сами. Занятые файлы пропускаются."))
        top = ctk.CTkFrame(card, fg_color="transparent")
        top.pack(fill="x", padx=20, pady=(14, 16))
        top.columnconfigure(0, weight=1)
        self.clean_total = ctk.CTkLabel(top, text="—", anchor="w", text_color=TEXT, font=font(30, True))
        self.clean_total.grid(row=0, column=0, sticky="w")
        self.clean_total_sub = ctk.CTkLabel(top, text=tr("можно освободить (выбранные пункты)"), anchor="w",
                                            text_color=MUTED, font=font(12))
        self.clean_total_sub.grid(row=1, column=0, sticky="w")
        self.clean_scan_btn = button(top, tr("Пересканировать"), "secondary", self._clean_scan, width=150)
        self.clean_scan_btn.grid(row=0, column=1, rowspan=2, padx=(0, 10))
        self.clean_btn = button(top, tr("Очистить выбранное"), "primary", self._clean_run, width=170)
        self.clean_btn.grid(row=0, column=2, rowspan=2)

        self._build_in_chunks("cleanup", cleaner.ITEMS, lambda it: self._clean_row(scroll, it),
                              done=lambda: self._on_show_cleanup())
        return page

    def _clean_row(self, scroll, it):
        """Строка одного пункта очистки (строки создаются порциями, см. _build_in_chunks)."""
        row = ctk.CTkFrame(scroll, corner_radius=14, fg_color=CARD, border_width=1, border_color=BORDER)
        row.pack(fill="x", padx=(0, 10), pady=6)
        row.columnconfigure(1, weight=1)
        allowed = self.admin or not it.admin
        var = ctk.BooleanVar(value=allowed)
        check = ctk.CTkCheckBox(row, text="", variable=var, width=24, command=self._clean_update_total,
                                fg_color=ACCENT, hover_color=ACCENT_HOVER, border_color="#3a4358",
                                checkbox_width=20, checkbox_height=20)
        check.grid(row=0, column=0, rowspan=2, padx=(18, 4))
        if not allowed:
            check.configure(state="disabled")
        ctk.CTkLabel(row, text=it.name, anchor="w", text_color=TEXT, font=font(15, True)
                     ).grid(row=0, column=1, sticky="w", padx=(4, 18), pady=(14, 0))
        autowrap(ctk.CTkLabel(row, text=it.description, anchor="w", justify="left", wraplength=540,
                              text_color=MUTED, font=font(12)), row, 190
                 ).grid(row=1, column=1, sticky="w", padx=(4, 18), pady=(3, 0))
        size = ctk.CTkLabel(row, text="", anchor="e", text_color=TEXT, font=font(15, True))
        size.grid(row=0, column=2, sticky="e", padx=20, pady=(14, 0))
        # под описанием, а не справа: справа длинная надпись обрезала бы описание
        note = ctk.CTkLabel(row, text="" if allowed else tr("нужны права администратора"), anchor="w",
                            text_color=MUTED if allowed else RED, font=font(11), height=16)
        note.grid(row=2, column=1, sticky="w", padx=(4, 18), pady=(2, 12))
        self.clean_rows[it.id] = {"var": var, "check": check, "size": size, "note": note, "allowed": allowed}

    def _on_show_cleanup(self):
        if len(self.clean_rows) < len(cleaner.ITEMS):
            return  # строки ещё дорисовываются: сканирование запустится, когда появится последняя
        if not self.clean_scanned and not self.clean_busy:
            self._clean_scan()

    def _clean_selected(self):
        return [i for i, r in self.clean_rows.items() if r["allowed"] and r["var"].get()]

    def _clean_update_total(self):
        known = [self.clean_sizes[i] for i in self._clean_selected() if self.clean_sizes.get(i) is not None]
        total = sum(v for v in self.clean_sizes.values() if v)
        self._badge("cleanup", cleaner.fmt_size(total) if self.clean_scanned and total else "")
        if not known and not self.clean_scanned:
            self.clean_total.configure(text="—")
            return
        start, end = self._clean_shown, sum(known)

        def step(t):  # сумма «набегает» до нового значения
            self._clean_shown = start + (end - start) * t
            self.clean_total.configure(text=cleaner.fmt_size(self._clean_shown))
        self._animate("clean_total", 500, step)

    def _clean_set_busy(self, busy):
        self.clean_busy = busy
        for b in (self.clean_scan_btn, self.clean_btn):
            b.configure(state="disabled" if busy else "normal")

    def _clean_scan(self):
        self._clean_set_busy(True)
        for i, r in self.clean_rows.items():
            if r["allowed"]:
                r["size"].configure(text=tr("считаю…"), text_color=MUTED)
                r["note"].configure(text="")
        self.clean_total.configure(text="…")
        self.jobs.put(("clean_scan",))

    def _clean_run(self):
        ids = [i for i in self._clean_selected() if self.clean_sizes.get(i)]
        if not ids:
            self._status(tr("Нечего очищать: выбери пункты, где что-то найдено."))
            return
        names = "\n".join(f"•  {cleaner.BY_ID[i].name}: {cleaner.fmt_size(self.clean_sizes[i])}" for i in ids)
        total = cleaner.fmt_size(sum(self.clean_sizes[i] for i in ids))
        text = tr("Удалить?\n\n{items}\n\nВсего примерно {size}. Вернуть эти файлы будет нельзя.")
        if not messagebox.askyesno(APP_NAME, text.format(items=names, size=total), parent=self):
            return
        self._clean_set_busy(True)
        self._status(tr("Очищаю…"))
        self.jobs.put(("clean_run", ids))

    # ---------- фоновый поток ----------

    def _scan_one(self, it):
        try:
            self.results.put(("clean_size", it.id, it.scan(), None))
        except Exception as e:
            self.results.put(("clean_size", it.id, None, str(e)))

    def _job_clean_scan(self):
        for it in cleaner.ITEMS:
            if self.admin or not it.admin:
                self._scan_one(it)
        self.results.put(("clean_done", None))

    def _job_clean_run(self, ids):
        total, errors = 0, []
        for i in ids:
            it = cleaner.BY_ID[i]
            self.results.put(("clean_progress", i))
            try:
                freed = it.clean()
                total += freed
                self.results.put(("clean_freed", i, freed))
                if it.warning:
                    errors.append(f"{it.name}: {it.warning}")
            except Exception as e:
                errors.append(f"{it.name}: {e}")
            self._scan_one(it)  # сколько осталось (занятые файлы)
        text = tr("Очистка завершена, освобождено {size}.").format(size=cleaner.fmt_size(total))
        if total:
            warning = cleaner.log(total)
            if warning:
                errors.append(warning)
        if errors:
            text += " " + tr("Ошибки: {list}").format(list="; ".join(errors))
        self.results.put(("message", text, bool(errors)))
        self.results.put(("clean_done", "history"))

    def _fail_clean_scan(self, *_args):
        self.results.put(("clean_done", None))

    _fail_clean_run = _fail_clean_scan

    # ---------- ответы ----------

    def _on_clean_size(self, cid, size, err):
        self.clean_sizes[cid] = size
        row = self.clean_rows[cid]
        if err:
            row["size"].configure(text="—", text_color=MUTED)
            row["note"].configure(text=tr("не удалось: {err}").format(err=err[:60]), text_color=RED)
        else:
            row["size"].configure(text=cleaner.fmt_size(size), text_color=TEXT if size else DIM)
        self._clean_update_total()

    def _on_clean_progress(self, cid):
        self.clean_rows[cid]["size"].configure(text=tr("очищаю…"), text_color=MUTED)

    def _on_clean_freed(self, cid, freed):
        self.clean_rows[cid]["note"].configure(text=tr("освобождено {size}").format(size=cleaner.fmt_size(freed)),
                                               text_color=GREEN)

    def _on_clean_done(self, then):
        self.clean_scanned = True
        self._clean_set_busy(False)
        self._clean_update_total()
        if then == "history":
            self._render_history()
