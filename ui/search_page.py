"""winopt/ui/search_page.py: поиск по всем твикам (поле в меню, результаты на отдельной странице).
Ищем по названию и описанию на обоих языках. Карточки в результатах — отдельные TweakRow того же твика:
переключаются как обычные, а состояние обновляется вместе с основной карточкой (см. TweaksMixin._on_state)."""
import customtkinter as ctk

from i18n import loc
from ui.theme import *  # noqa: F403
from ui.tweaks_page import TweakRow

SEARCH_LIMIT = 40  # больше карточек customtkinter рисует медленно


class SearchMixin:

    def _build_search_entry(self, side):
        self.search_entry = ctk.CTkEntry(side, placeholder_text=tr("Поиск твиков…"), height=32, corner_radius=9,
                                         fg_color=CARD, border_color=BORDER, text_color=TEXT, font=font(12))
        self.search_entry.pack(fill="x", padx=(14, 18), pady=(0, 6))
        self.search_entry.bind("<KeyRelease>", self._search_changed)
        self.search_entry.bind("<Escape>", lambda _e: self._search_clear())

    def _build_search_page(self, box):
        page = ctk.CTkFrame(box, fg_color="transparent")
        self.search_box = ctk.CTkFrame(self._scroll(page), fg_color="transparent")
        self.search_box.pack(fill="x")
        self.titles["search"], self.subtitles["search"] = "", ""
        return page

    def _search_changed(self, event=None):
        if event is not None and event.keysym == "Escape":
            return
        if self._search_job:
            self.after_cancel(self._search_job)
        self._search_job = self.after(250, self._search_run)  # не перерисовывать на каждую букву

    def _search_clear(self):
        self.search_entry.delete(0, "end")
        self._search_run()

    @staticmethod
    def _haystack(t):
        return " ".join(str(t.get(k) or "") for k in ("name", "name_en", "description", "description_en")).lower()

    def _search_run(self):
        self._search_job = None
        q = self.search_entry.get().strip().lower()
        if not q:
            if self.current == "search":
                self._show(self._search_back or self.sections[0])
            return
        if self.current != "search":
            self._search_back = self.current

        found = [t for t in self.tweaks.values() if q in self._haystack(t)]
        self._clear(self.search_box)
        self.search_rows = {}
        if not found:
            self._note(self.search_box, tr("Ничего не найдено. Попробуй другое слово — ищу по названию и описанию."))
        shown = 0
        for cat in self.cats:
            group = [t for t in found if t.get("category") == cat]
            if not group or shown >= SEARCH_LIMIT:
                continue
            ctk.CTkLabel(self.search_box, text=self.titles[cat].upper(), anchor="w", text_color=DIM,
                         font=font(10, True)).pack(fill="x", padx=4, pady=(10, 2))
            for t in group[:SEARCH_LIMIT - shown]:
                row = TweakRow(self.search_box, t, self.admin, self._toggle)
                row.pack(fill="x", padx=(0, 10), pady=4)
                main_state = self.rows[t["id"]].state
                if main_state != "loading":
                    row.set_state(main_state)
                self.search_rows[t["id"]] = row
                shown += 1
        if len(found) > SEARCH_LIMIT:
            self._note(self.search_box, tr("Показаны первые {n}. Уточни запрос.").format(n=SEARCH_LIMIT))

        self.titles["search"] = tr("Поиск: «{q}»").format(q=self.search_entry.get().strip())
        self.subtitles["search"] = tr("Найдено твиков: {n}").format(n=len(found))
        if self.current == "search":
            self.title_lbl.configure(text=self.titles["search"])
            self.subtitle_lbl.configure(text=self.subtitles["search"])
        else:
            self._show("search")
