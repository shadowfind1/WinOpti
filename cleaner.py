"""winopt/cleaner.py: очистка диска.

Каждый пункт (Item) умеет:
  scan()  -> int   сколько байт можно освободить
  clean() -> int   удалить и вернуть, сколько байт реально освобождено
Удаляется только то, что Windows и программы создают заново сами. Занятые файлы пропускаются.
"""
import ctypes
import os
import re
import stat
import time
from ctypes import wintypes
from pathlib import Path

import engine
from i18n import tr

WINDIR = Path(os.environ.get("SystemRoot", r"C:\Windows"))
PROGRAMDATA = Path(os.environ.get("ProgramData", r"C:\ProgramData"))
HOUR = 3600
FILE_ATTRIBUTE_REPARSE_POINT = 0x400


def fmt_size(n):
    b, kb, mb, gb = tr("Б"), tr("КБ"), tr("МБ"), tr("ГБ")
    for unit in (b, kb, mb):
        if n < 1024:
            return f"{n:.0f} {unit}" if unit == b else f"{n:.1f} {unit}"
        n /= 1024
    return f"{n:.2f} {gb}"


# ---------- файлы и папки ----------

def _walk(roots, min_age):
    """-> (файлы [(путь, размер)], папки). В ссылки, junction и прочие reparse-точки не заходим:
    иначе ссылка из Temp на чужую папку удалила бы её содержимое."""
    now = time.time()
    files, dirs = [], []
    stack = []
    for root in roots:
        try:
            st = os.stat(root, follow_symlinks=False)
        except OSError:
            continue
        if st.st_file_attributes & FILE_ATTRIBUTE_REPARSE_POINT:
            continue
        if stat.S_ISDIR(st.st_mode):
            stack.append(str(root))
        elif now - st.st_mtime >= min_age:
            files.append((str(root), st.st_size))
    while stack:
        d = stack.pop()
        try:
            it = os.scandir(d)
        except OSError:
            continue
        with it:
            for e in it:
                try:
                    st = e.stat(follow_symlinks=False)
                    if st.st_file_attributes & FILE_ATTRIBUTE_REPARSE_POINT:
                        continue
                    if stat.S_ISDIR(st.st_mode):
                        stack.append(e.path)
                        dirs.append(e.path)
                    elif now - st.st_mtime >= min_age:
                        files.append((e.path, st.st_size))
                except OSError:
                    continue
    return files, dirs


def _remove(path):
    try:
        os.remove(path)
    except PermissionError:
        os.chmod(path, stat.S_IWRITE)  # «только чтение» мешает удалению
        os.remove(path)


def _delete(roots, min_age):
    files, dirs = _walk(roots, min_age)
    freed = 0
    for path, size in files:
        try:
            _remove(path)
            freed += size
        except OSError:
            pass  # файл занят программой: пропускаем
    for d in sorted(dirs, key=len, reverse=True):  # сначала самые глубокие
        try:
            os.rmdir(d)  # удалится только пустая
        except OSError:
            pass
    return freed


def _check_temp(path):
    """Защита от странного %TEMP% (например, C:\\): чистим только папку с именем Temp/Tmp."""
    if path.name.lower() not in ("temp", "tmp"):
        raise RuntimeError(tr("подозрительная папка {path}, пропускаю").format(path=path))
    return path


class Item:
    def __init__(self, id, name, description, admin, roots=None, min_age=0):
        self.id, self.name, self.description, self.admin = id, name, description, admin
        self._roots, self.min_age = roots, min_age
        self.warning = None  # заполняет clean(), если очистка прошла, но не всё гладко

    def roots(self):
        return self._roots()

    def scan(self):
        return sum(size for _, size in _walk(self.roots(), self.min_age)[0])

    def clean(self):
        return _delete(self.roots(), self.min_age)


# ---------- кэш обновлений Windows ----------

UPDATE_SERVICES = ("wuauserv", "bits")


def _svc_running(name):
    _, out = engine.run(["sc.exe", "query", name])
    return re.search(r"STATE\s*:\s*4\b", out) is not None  # 4 = RUNNING, не зависит от языка


def _svc_stop(name):
    engine.run(["powershell", "-NoProfile", "-Command", f"Stop-Service -Name {name} -Force"])


def _svc_start(name):
    """-> True, если служба после запуска работает."""
    engine.run(["powershell", "-NoProfile", "-Command", f"Start-Service -Name {name}"])
    return _svc_running(name)


class UpdateCache(Item):
    """Скачанные файлы обновлений. Службы обновления на время очистки останавливаем,
    иначе файлы заняты, потом запускаем те, что работали."""

    def clean(self):
        self.warning = None
        was_running = [s for s in UPDATE_SERVICES if _svc_running(s)]
        for s in was_running:
            _svc_stop(s)
        try:
            return super().clean()
        finally:
            failed = [s for s in was_running if not _svc_start(s)]
            if failed:
                self.warning = tr("службы {list} не запустились обратно; Windows запустит их сама при проверке "
                                  "обновлений или после перезагрузки").format(list=", ".join(failed))


# ---------- кэш оптимизации доставки ----------

class DeliveryCache(Item):
    def clean(self):
        before = self.scan()
        rc, out = engine.run(["powershell", "-NoProfile", "-Command",
                              "Delete-DeliveryOptimizationCache -Force -ErrorAction Stop"])
        if rc != 0:
            raise RuntimeError(out.strip().splitlines()[0] if out.strip() else tr("код {rc}").format(rc=rc))
        return max(before - self.scan(), 0)


# ---------- корзина ----------

if ctypes.sizeof(ctypes.c_void_p) == 4:
    class _RBINFO(ctypes.Structure):  # в 32-битной Windows структура упакована без выравнивания
        _pack_ = 1
        _fields_ = [("cbSize", wintypes.DWORD), ("i64Size", ctypes.c_longlong),
                    ("i64NumItems", ctypes.c_longlong)]
else:
    class _RBINFO(ctypes.Structure):
        _fields_ = [("cbSize", wintypes.DWORD), ("i64Size", ctypes.c_longlong),
                    ("i64NumItems", ctypes.c_longlong)]


class RecycleBin(Item):
    def scan(self):
        info = _RBINFO(cbSize=ctypes.sizeof(_RBINFO))
        hr = ctypes.windll.shell32.SHQueryRecycleBinW(None, ctypes.byref(info))
        if hr != 0:
            raise OSError(f"SHQueryRecycleBin: 0x{hr & 0xFFFFFFFF:08x}")
        return info.i64Size

    def clean(self):
        before = self.scan()
        if before == 0:
            return 0
        # SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
        hr = ctypes.windll.shell32.SHEmptyRecycleBinW(None, None, 0x7) & 0xFFFFFFFF
        if hr not in (0, 0x8000FFFF):  # E_UNEXPECTED = корзина уже пуста
            raise OSError(f"SHEmptyRecycleBin: 0x{hr:08x}")
        return max(before - self.scan(), 0)


# ---------- список ----------

ITEMS = [
    Item("user_temp", tr("Временные файлы пользователя"),
         tr("Папка %TEMP%: остатки установщиков, распаковки архивов, кэш программ. "
            "Файлы младше часа не трогаем, чтобы не помешать работающим программам."),
         admin=False, roots=lambda: [_check_temp(Path(os.environ.get("TEMP", "")))], min_age=HOUR),
    Item("windows_temp", tr("Временные файлы Windows"),
         tr("Папка Windows\\Temp: служебные временные файлы системы и установщиков. Файлы младше часа не трогаем."),
         admin=True, roots=lambda: [WINDIR / "Temp"], min_age=HOUR),
    UpdateCache("update_cache", tr("Кэш обновлений Windows"),
                tr("Уже установленные обновления, скачанные в SoftwareDistribution\\Download. "
                   "Не запускай очистку, пока Windows устанавливает обновления."),
                admin=True, roots=lambda: [WINDIR / "SoftwareDistribution" / "Download"]),
    DeliveryCache("delivery_cache", tr("Кэш оптимизации доставки"),
                  tr("Копии обновлений и приложений, которые Windows хранит, чтобы раздавать другим ПК."),
                  admin=True, roots=lambda: [WINDIR / "ServiceProfiles" / "NetworkService" / "AppData" /
                                             "Local" / "Microsoft" / "Windows" / "DeliveryOptimization" /
                                             "Cache"]),
    Item("error_reports", tr("Отчёты об ошибках и дампы памяти"),
         tr("Отчёты о сбоях программ и дампы памяти после «синих экранов». "
            "Нужны только для диагностики, могут занимать гигабайты."),
         admin=True, roots=lambda: [PROGRAMDATA / "Microsoft" / "Windows" / "WER" / d
                                    for d in ("ReportArchive", "ReportQueue", "Temp")]
                                   + [WINDIR / "Minidump", WINDIR / "MEMORY.DMP"]),
    RecycleBin("recycle_bin", tr("Корзина"),
               tr("Файлы, удалённые в корзину, на всех дисках. После очистки их уже не вернуть."),
               admin=False),
]
BY_ID = {it.id: it for it in ITEMS}


def log(total):
    """Запись в общий журнал WinOpti. -> текст предупреждения или None."""
    return engine._log({"id": "cleanup", "name": f"Очистка диска: освобождено {fmt_size(total)}"}, "clean")
