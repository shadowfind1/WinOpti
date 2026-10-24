"""winopt/ui/search_page.py: поиск по всем твикам (поле в меню, результаты на отдельной странице).
Ищем по названию и описанию на обоих языках. Карточки в результатах — отдельные TweakRow того же твика:
переключаются как обычные, а состояние обновляется вместе с основной карточкой (см. TweaksMixin._on_state)."""
import re

import customtkinter as ctk

from ui.theme import *  # noqa: F403

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
        fields = ("name", "name_en", "description", "description_en", "keywords")  # keywords: слова, которых нет в тексте
        return " ".join(str(t.get(k) or "") for k in fields).lower()

    @staticmethod
    def _terms(q):
        """Слова запроса без окончаний: «мышка», «мышь» и «мыши» -> «мыш». Для английского — без «s» на конце.
        Грубо, но без словарей: отрезаем у русского слова до двух последних букв, оставляя минимум три."""
        terms = []
        for w in q.split():
            if re.search("[а-яё]", w) and len(w) >= 4:
                w = w[:max(3, len(w) - 2)]
            elif len(w) > 3 and w.endswith("s"):
                w = w[:-1]
            terms.append(w)
        return terms

    def _search_run(self):
        self._search_job = None
        q = self.search_entry.get().strip().lower()
        if not q:
            if self.current == "search":
                self._show(self._search_back or self.sections[0])
            return
        if self.current != "search":
            self._search_back = self.current
        self._page("search")  # страница строится лениво

        terms = self._terms(q)
        found = [t for t in self.tweaks.values() if all(term in self._haystack(t) for term in terms)]
        self._clear(self.search_box)
        self.search_rows = {}
        if not found:
            self._note(self.search_box, tr("Ничего не найдено. Попробуй другое слово — ищу по названию и описанию."))
        # порядок как в меню: заголовок вкладки, под ним её твики
        items = []
        for cat in self.cats:
            group = [t for t in found if t.get("category") == cat][:SEARCH_LIMIT - sum(k == "row" for k, _ in items)]
            if group:
                items += [("header", cat)] + [("row", t) for t in group]

        def make(item):
            kind, value = item
            if kind == "header":
                ctk.CTkLabel(self.search_box, text=self.titles[value].upper(), anchor="w", text_color=DIM,
                             font=font(10, True)).pack(fill="x", padx=4, pady=(10, 2))
            else:
                row = self._new_row(self.search_box, value)
                row.pack(fill="x", padx=(0, 10), pady=4)
                self.search_rows[value["id"]] = row

        def done():
            if len(found) > SEARCH_LIMIT:
                self._note(self.search_box, tr("Показаны первые {n}. Уточни запрос.").format(n=SEARCH_LIMIT))
        self._build_in_chunks("search", items, make, done=done)

        self.titles["search"] = tr("Поиск: «{q}»").format(q=self.search_entry.get().strip())
        self.subtitles["search"] = tr("Найдено твиков: {n}").format(n=len(found))
        if self.current == "search":
            self.title_lbl.configure(text=self.titles["search"])
            self.subtitle_lbl.configure(text=self.subtitles["search"])
        else:
            self._show("search")
