"""winopt/build.py: сборка WinOpti.exe.

  python build.py      ->  dist/WinOpti.exe  (один файл: его и выкладываем в Releases)

Один exe при каждом запуске распаковывает себя в папку %TEMP%/_MEIxxxxx — поэтому «Очистка» такие папки
пропускает (cleaner.RUNNING_APP_DIR). Права администратора запрашиваются манифестом (--uac-admin).
Нужно: pip install pyinstaller
"""
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DIST, WORK = ROOT / "dist", ROOT / "build"
ICON = ROOT / "assets" / "winopti.ico"


def make_icon():
    """Иконка из того же логотипа, что в окне (ui.theme.logo_pil), во всех размерах Windows."""
    sys.path.insert(0, str(ROOT))
    from ui.theme import logo_pil
    ICON.parent.mkdir(exist_ok=True)
    logo_pil(256).save(ICON, sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])


def build():
    sep = os.pathsep
    cmd = [sys.executable, "-m", "PyInstaller", str(ROOT / "gui.py"),
           "--name", "WinOpti", "--onefile", "--noconfirm", "--clean", "--windowed", "--uac-admin",
           "--icon", str(ICON),
           "--add-data", f"{ROOT / 'tweaks.json'}{sep}.",
           "--add-data", f"{ICON}{sep}assets",
           "--collect-data", "customtkinter",
           "--hidden-import", "lang_en",  # импортируется только при английском языке
           "--distpath", str(DIST), "--workpath", str(WORK), "--specpath", str(WORK)]
    subprocess.run(cmd, check=True, cwd=ROOT)


if __name__ == "__main__":
    shutil.rmtree(DIST / "WinOpti", ignore_errors=True)  # старая сборка папкой
    make_icon()
    build()
    exe = DIST / "WinOpti.exe"
    print(f"\nГотово: {exe} ({exe.stat().st_size / 1024 / 1024:.1f} МБ)")
