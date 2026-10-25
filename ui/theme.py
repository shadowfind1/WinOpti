"""winopt/ui/theme.py: палитра, шрифты, иконки, кнопки и функции анимации.
Остальные модули ui берут отсюда всё через `from ui.theme import *` (список в __all__)."""
import functools
import math
import sys
import tkinter
from pathlib import Path

import customtkinter as ctk

from i18n import tr

try:
    from PIL import Image, ImageDraw, ImageFilter
except ImportError:  # без Pillow всё работает, только без иконок
    Image = ImageDraw = ImageFilter = None

__all__ = ["APP_NAME", "BG", "SIDEBAR", "CARD", "CARD_ALT", "BORDER", "HOVER", "SELECTED", "TEXT", "MUTED", "DIM",
           "ACCENT", "ACCENT_HOVER", "GREEN", "ORANGE", "RED", "PILL_GREEN", "PILL_ORANGE", "PILL_RED", "PILL_GRAY",
           "PILL_BLUE", "RISK", "font", "ease", "mix", "autowrap", "icon", "logo_image", "pill", "button", "text", "tr"]

APP_NAME = "WinOpti"
FONT = "Segoe UI"

# палитра
BG, SIDEBAR, CARD, CARD_ALT = "#0e1116", "#12151c", "#171b24", "#1d222d"
BORDER, HOVER, SELECTED = "#252b3a", "#1a1f2b", "#1c2231"
TEXT, MUTED, DIM = "#e8ebf2", "#8b93a7", "#5d667c"
ACCENT, ACCENT_HOVER, GREEN, ORANGE, RED = "#5b8cff", "#7aa2ff", "#3ecf8e", "#f5a623", "#ff6b6b"
PILL_GREEN, PILL_ORANGE, PILL_RED, PILL_GRAY, PILL_BLUE = "#123325", "#3a2b10", "#3a1b1b", "#222836", "#1a2540"

RISK = {"low": (tr("низкий риск"), GREEN), "medium": (tr("средний риск"), ORANGE), "high": (tr("высокий риск"), RED)}


def font(size=13, bold=False):
    return ctk.CTkFont(family=FONT, size=size, weight="bold" if bold else "normal")


# ---------- анимации ----------

def ease(t):
    """Плавное замедление к концу (ease-out cubic)."""
    return 1 - (1 - t) ** 3


def mix(a, b, t):
    """Цвет между a и b (#rrggbb), t от 0 до 1."""
    ca = [int(a[i:i + 2], 16) for i in (1, 3, 5)]
    cb = [int(b[i:i + 2], 16) for i in (1, 3, 5)]
    return "#%02x%02x%02x" % tuple(round(x + (y - x) * t) for x, y in zip(ca, cb))


def autowrap(label, container, margin):
    """Перенос текста по реальной ширине контейнера (а не по фиксированному числу пикселей):
    на узком окне текст не обрезается, на широком карточки становятся ниже."""
    scale = ctk.ScalingTracker.get_widget_scaling(container)
    native = not isinstance(label, ctk.CTkBaseClass)  # простой tkinter.Label (см. text) — в настоящих пикселях

    def on_resize(event):
        wrap = max(200, int(event.width / scale) - margin)
        try:
            label.configure(wraplength=round(wrap * scale) if native else wrap)
        except Exception:
            pass  # подпись уже уничтожена
    container.bind("<Configure>", on_resize, add="+")
    return label


# ---------- иконки (рисуем сами, чтобы не таскать файлы) ----------

K = 8  # рисуем в 8 раз крупнее и уменьшаем: получается сглаживание


def _pts(points):
    return [(x * K, y * K) for x, y in points]


def _rot_rect(cx, cy, w, h, angle):
    a = math.radians(angle)
    pts = []
    for dx, dy in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)):
        pts.append((cx + dx * math.cos(a) - dy * math.sin(a), cy + dx * math.sin(a) + dy * math.cos(a)))
    return pts


def _icon_mask(kind):
    """Маска иконки на сетке 24x24: 255 = рисуем, 0 = вырез."""
    size = 24 * K
    mask = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(mask)
    if kind == "privacy":  # щит с галочкой
        d.polygon(_pts([(12, 2), (20, 5), (20, 12), (18.5, 16), (12, 22), (5.5, 16), (4, 12), (4, 5)]), fill=255)
        d.line(_pts([(8.3, 12.2), (11, 14.8), (15.8, 9.6)]), fill=0, width=int(2 * K), joint="curve")
    elif kind == "interface":  # четыре плитки
        for x, y in ((3, 3), (13.5, 3), (3, 13.5), (13.5, 13.5)):
            d.rounded_rectangle([x * K, y * K, (x + 7.5) * K, (y + 7.5) * K], radius=2 * K, fill=255)
    elif kind == "appearance":  # палитра художника
        d.ellipse([2.5 * K, 2.5 * K, 21.5 * K, 21.5 * K], fill=255)
        for cx, cy in ((7.6, 9), (12, 6.4), (16.6, 8.6)):
            d.ellipse([(cx - 1.7) * K, (cy - 1.7) * K, (cx + 1.7) * K, (cy + 1.7) * K], fill=0)
        d.ellipse([12.6 * K, 13.4 * K, 18.6 * K, 19.4 * K], fill=0)
    elif kind == "explorer":  # папка
        d.rounded_rectangle([2 * K, 4.5 * K, 10.5 * K, 9 * K], radius=1.6 * K, fill=255)
        d.rounded_rectangle([2 * K, 7 * K, 22 * K, 20 * K], radius=2.6 * K, fill=255)
    elif kind == "performance":  # молния
        d.polygon(_pts([(14, 2), (5, 13), (11.5, 13), (10, 22), (19, 10.5), (12.5, 10.5)]), fill=255)
    elif kind == "gaming":  # геймпад
        d.rounded_rectangle([2 * K, 6.5 * K, 22 * K, 18.5 * K], radius=5.5 * K, fill=255)
        d.line(_pts([(5.2, 12.5), (9.8, 12.5)]), fill=0, width=int(1.8 * K))
        d.line(_pts([(7.5, 10.2), (7.5, 14.8)]), fill=0, width=int(1.8 * K))
        d.ellipse([14.6 * K, 10.2 * K, 16.8 * K, 12.4 * K], fill=0)
        d.ellipse([17.2 * K, 12.6 * K, 19.4 * K, 14.8 * K], fill=0)
    elif kind == "system":  # шестерёнка
        d.ellipse([5 * K, 5 * K, 19 * K, 19 * K], fill=255)
        for ang in (0, 45, 90, 135):
            d.polygon(_pts(_rot_rect(12, 12, 4, 21, ang)), fill=255)
        d.ellipse([8.6 * K, 8.6 * K, 15.4 * K, 15.4 * K], fill=0)
    elif kind == "presets":  # звезда
        star = []
        for i in range(10):
            r = 10 if i % 2 == 0 else 4.4
            a = math.radians(-90 + i * 36)
            star.append((12 + r * math.cos(a), 12.6 + r * math.sin(a)))
        d.polygon(_pts(star), fill=255)
    elif kind == "apps":  # коробка
        d.rounded_rectangle([2 * K, 3.5 * K, 22 * K, 8.6 * K], radius=1.6 * K, fill=255)
        d.rounded_rectangle([3.5 * K, 9.8 * K, 20.5 * K, 20.5 * K], radius=1.8 * K, fill=255)
        d.rounded_rectangle([9 * K, 12.4 * K, 15 * K, 14.8 * K], radius=1 * K, fill=0)
    elif kind == "startup":  # кнопка питания
        d.arc([3.6 * K, 4.4 * K, 20.4 * K, 21.2 * K], start=300, end=240, fill=255, width=int(2.4 * K))
        d.line(_pts([(12, 2.4), (12, 11.6)]), fill=255, width=int(2.4 * K))
    elif kind == "cleanup":  # мусорная корзина
        d.rounded_rectangle([9.5 * K, 2.6 * K, 14.5 * K, 5.6 * K], radius=1 * K, fill=255)
        d.rounded_rectangle([3.5 * K, 4.8 * K, 20.5 * K, 7.4 * K], radius=1.2 * K, fill=255)
        d.polygon(_pts([(5.4, 8.8), (18.6, 8.8), (17.4, 20), (16, 21.4), (8, 21.4), (6.6, 20)]), fill=255)
        for x in (9.6, 12, 14.4):
            d.line(_pts([(x, 11.6), (x, 18.4)]), fill=0, width=int(1.4 * K))
    elif kind == "restore":  # часы с круговой стрелкой
        box = [3.2 * K, 3.2 * K, 20.8 * K, 20.8 * K]
        d.arc(box, start=100, end=360, fill=255, width=int(2.4 * K))
        d.arc(box, start=0, end=12, fill=255, width=int(2.4 * K))
        d.line(_pts([(12, 12), (12, 7.5)]), fill=255, width=int(2 * K))
        d.line(_pts([(12, 12), (15.2, 13.6)]), fill=255, width=int(2 * K))
    return mask


_icon_cache = {}


def icon(kind, color, size=20):
    if Image is None:
        return None
    key = (kind, color, size)
    if key not in _icon_cache:
        mask = _icon_mask(kind).resize((size * 2, size * 2), Image.LANCZOS)
        solid = Image.new("RGBA", mask.size, color)
        solid.putalpha(mask)
        _icon_cache[key] = ctk.CTkImage(light_image=solid, dark_image=solid, size=(size, size))
    return _icon_cache[key]


LOGO_FROM, LOGO_TO = "#5b5bff", "#c04dff"  # градиент плитки логотипа


@functools.lru_cache(maxsize=1)
def _logo_master():
    """Логотип 1024×1024: двойной шеврон вверх («буст») на скруглённом квадрате с диагональным градиентом,
    мягким бликом сверху, светлой кромкой и тенью под знаком. Рисуем один раз и уменьшаем."""
    s = 1024
    white = Image.new("L", (s, s), 255)

    def layer(alpha):  # белый слой с заданной прозрачностью
        return Image.merge("RGBA", (white, white, white, alpha))

    tile = Image.new("L", (s, s), 0)
    ImageDraw.Draw(tile).rounded_rectangle([0, 0, s - 1, s - 1], radius=s * .27, fill=255)
    across = Image.linear_gradient("L").rotate(90).transpose(Image.FLIP_LEFT_RIGHT).resize((s, s))
    down = Image.linear_gradient("L").resize((s, s))
    grad = Image.composite(Image.new("RGBA", (s, s), LOGO_TO), Image.new("RGBA", (s, s), LOGO_FROM),
                           Image.blend(across, down, .5))
    out = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    out.paste(grad, (0, 0), tile)
    shine = down.point(lambda v: max(0, 70 - v // 2))  # блик: светлее сверху, к середине сходит на нет
    out = Image.alpha_composite(out, layer(Image.composite(shine, Image.new("L", (s, s), 0), tile)))
    edge = Image.new("L", (s, s), 0)
    ImageDraw.Draw(edge).rounded_rectangle([6, 6, s - 7, s - 7], radius=s * .27 - 6, outline=60, width=10)
    out = Image.alpha_composite(out, layer(edge))

    mark = Image.new("L", (s, s), 0)
    d = ImageDraw.Draw(mark)
    w = s * .105
    for top in (.36, .58):
        pts = [(s * .27, s * (top + .17)), (s * .5, s * top), (s * .73, s * (top + .17))]
        d.line(pts, fill=255, width=round(w), joint="curve")
        for x, y in (pts[0], pts[2]):  # скруглённые концы
            d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=255)
    shadow = mark.filter(ImageFilter.GaussianBlur(s * .025)).point(lambda v: v * 90 // 255)
    shadow = Image.merge("RGBA", (*[Image.new("L", (s, s), 10)] * 3, shadow))
    out = Image.alpha_composite(out, shadow.transform((s, s), Image.AFFINE, (1, 0, 0, 0, 1, -s * .02)))
    out.paste(Image.new("RGBA", (s, s), "#ffffff"), (0, 0), mark)
    return out


LOGO_PNG = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent)) / "assets" / "logo.png"


def logo_pil(px):
    """Логотип как картинка PIL px×px: для окна и для .ico. Готовую картинку (её сохраняет build.py) загрузить
    быстрее, чем рисовать заново (~60 мс на каждом запуске)."""
    try:
        base = Image.open(LOGO_PNG).convert("RGBA") if px <= 256 else _logo_master()
    except OSError:
        base = _logo_master()
    return base.resize((px, px), Image.LANCZOS)


def logo_image(size=38):
    if Image is None:
        return None
    base = logo_pil(size * 2)
    return ctk.CTkImage(light_image=base, dark_image=base, size=(size, size))


# ---------- мелкие виджеты ----------

def pill(master, text, bg, fg):
    return ctk.CTkLabel(master, text=f"  {text}  ", fg_color=bg, text_color=fg,
                        corner_radius=9, height=20, font=font(11, True))


def text(master, value, size=12, bold=False, color=TEXT, bg=CARD, wrap=0):
    """Простая надпись Tk: одно окно Windows вместо трёх у CTkLabel. Для текста на карточках, которых
    на странице десятки: чем меньше окон, тем быстрее окно двигается и перерисовывается.
    Размеры — в логических пикселях, как у customtkinter (учитываем масштаб экрана)."""
    scale = ctk.ScalingTracker.get_widget_scaling(master)
    return tkinter.Label(master, text=value, bg=bg, fg=color, anchor="w", justify="left", bd=0, padx=0, pady=0,
                         highlightthickness=0, wraplength=round(wrap * scale),
                         font=(FONT, -round(size * scale), "bold" if bold else "normal"))


def button(master, text, kind, command, width=None, height=36):
    fg, hover, txt = {
        "primary": (ACCENT, ACCENT_HOVER, "#ffffff"),
        "secondary": ("#232938", "#2d3548", TEXT),
        "danger": ("#3a1c1c", "#4c2424", RED),
    }[kind]
    kw = {"width": width} if width else {}
    return ctk.CTkButton(master, text=text, command=command, height=height, corner_radius=10,
                         fg_color=fg, hover_color=hover, text_color=txt, font=font(13, True), **kw)
