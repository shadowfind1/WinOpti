"""winopt/build.py: сборка WinOpti.exe.

  python build.py      ->  dist/WinOpti/WinOpti.exe  и  dist/WinOpti-<дата>.zip

Сборка «папкой» (onedir), а не одним exe: один exe при каждом запуске распаковывается в %TEMP%,
медленнее стартует, чаще пугает антивирусы, а «Очистка» WinOpti может удалить его распакованные
файлы прямо во время работы. Права администратора запрашиваются манифестом (--uac-admin).
Нужно: pip install pyinstaller
"""
import os
import shutil
import subprocess
import sys
import zipfile
from datetime import datetime
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
           "--name", "WinOpti", "--noconfirm", "--clean", "--windowed", "--uac-admin",
           "--icon", str(ICON),
           "--add-data", f"{ROOT / 'tweaks.json'}{sep}.",
           "--add-data", f"{ICON}{sep}assets",
           "--collect-data", "customtkinter",
           "--hidden-import", "lang_en",  # импортируется только при английском языке
           "--distpath", str(DIST), "--workpath", str(WORK), "--specpath", str(WORK)]
    subprocess.run(cmd, check=True, cwd=ROOT)


def pack():
    folder = DIST / "WinOpti"
    archive = DIST / f"WinOpti-{datetime.now():%Y-%m-%d}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
        for f in folder.rglob("*"):
            z.write(f, Path("WinOpti") / f.relative_to(folder))
    return archive


if __name__ == "__main__":
    shutil.rmtree(DIST / "WinOpti", ignore_errors=True)
    make_icon()
    build()
    archive = pack()
    exe = DIST / "WinOpti" / "WinOpti.exe"
    size = sum(f.stat().st_size for f in (DIST / "WinOpti").rglob("*") if f.is_file())
    print(f"\nГотово: {exe}\nПапка: {size / 1024 / 1024:.1f} МБ, архив: {archive} "
          f"({archive.stat().st_size / 1024 / 1024:.1f} МБ)")
