# Логотип «Арена»: голубой скруглённый квадрат, две тёмные стрелки вверх.
# Пишет Assets/logo.png (512 px) и Assets/winopti.ico (16–256 px). Запуск из папки WinOpti2.
from PIL import Image, ImageDraw

ACCENT = (59, 232, 255, 255)
INK = (6, 8, 15, 255)


def draw(size):
    k = 8  # рисуем крупно и уменьшаем — сглаженные края
    s = size * k
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.24), fill=ACCENT)
    u = s / 24  # сетка 24×24, как у значка в макете
    w = max(int(u * 2.9), 1)
    for y in (12.5, 18.5):
        pts = [(5 * u, y * u), (12 * u, (y - 7) * u), (19 * u, y * u)]
        d.line(pts, fill=INK, width=w, joint="curve")
        for x, yy in (pts[0], pts[2]):  # скруглённые концы
            d.ellipse([x - w / 2, yy - w / 2, x + w / 2, yy + w / 2], fill=INK)
    return img.resize((size, size), Image.LANCZOS)


draw(512).save("Assets/logo.png")
sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
big = draw(256)
big.save("Assets/winopti.ico", sizes=[(n, n) for n in sizes], append_images=[draw(n) for n in sizes[:-1]])
print("ok")
