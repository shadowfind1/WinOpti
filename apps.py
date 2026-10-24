"""winopt/apps.py: удаление программ «с корнем» и встроенных приложений Windows.

Программы: сначала их собственный деинсталлятор, потом поиск остатков (папки, ярлыки,
разделы реестра). Правила безопасности:
  - остатки ищем только по точному совпадению имени (папка «Google» не остаток «Google Chrome»);
  - не предлагаем папку, внутри которой лежит другая установленная программа;
  - системные папки и корни (Program Files, AppData, …) защищены;
  - папку установки внутри профиля пользователя предлагаем, только если её имя совпадает
    с программой (у TLauncher папка установки .minecraft, а там миры);
  - файлы удаляем в корзину, разделы реестра перед удалением сохраняем в .reg.
Встроенные приложения: Remove-AppxPackage для всех пользователей + удаление «заготовки»,
чтобы приложение не вернулось у новых пользователей и после обновлений.
"""
import ctypes
import json
import os
import re
import subprocess
import winreg
from ctypes import wintypes
from datetime import datetime
from pathlib import Path

import cleaner
import engine
import startup
from i18n import tr

UNINSTALL = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
UNINSTALL32 = "Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall"
REG_BACKUP_DIR = engine.DATA_DIR / "reg_backups"


def _p(*parts):
    """Путь из переменной окружения (+ подпапки) или None."""
    base = os.environ.get(parts[0])
    return Path(base, *parts[1:]) if base else None


HOME = Path.home()
WINDIR = _p("SystemRoot") or Path(r"C:\Windows")
PF, PF86, PD = _p("ProgramFiles"), _p("ProgramFiles(x86)"), _p("ProgramData")
APPDATA, LOCAL = _p("APPDATA"), _p("LOCALAPPDATA")
START_USER = _p("APPDATA", "Microsoft", "Windows", "Start Menu", "Programs")
START_ALL = _p("ProgramData", "Microsoft", "Windows", "Start Menu", "Programs")
DESKTOPS = [HOME / "Desktop", _p("PUBLIC", "Desktop")]
LOCAL_PROGRAMS = LOCAL / "Programs" if LOCAL else None

# где ищем остатки по имени папки
FOLDER_BASES = [b for b in (PF, PF86, PD, APPDATA, LOCAL, LOCAL_PROGRAMS, START_USER, START_ALL) if b]


def _key(path):
    return os.path.normcase(os.path.normpath(str(path)))


PROTECTED = {_key(p) for p in FOLDER_BASES + [
    HOME, HOME / "Desktop", HOME / "Documents", HOME / "Downloads", HOME / "AppData", _p("PUBLIC"),
    PF and PF / "Common Files", PF86 and PF86 / "Common Files", LOCAL and LOCAL / "Temp",
    APPDATA and APPDATA / "Microsoft", LOCAL and LOCAL / "Microsoft", PD and PD / "Microsoft",
    LOCAL and LOCAL / "Packages", WINDIR] if p}
USER_ROOTS = [_key(HOME)]
REG_PROTECTED = {"microsoft", "classes", "policies", "wow6432node", "clients", "registeredapplications",
                 "odbc", "windows", "intel", "nvidia", "nvidiacorporation"}
GENERIC = {"bin", "app", "apps", "program", "programs", "common", "files", "data", "temp", "tmp", "update",
           "updater", "launcher", "client", "setup", "install", "installer", "tools", "live", "application",
           "x64", "x86", "plugins", "driver", "drivers", "games", "game", "software", "user", "users"}


# ---------- имена ----------

def _norm(s):
    return re.sub(r"[^0-9a-zа-яё]", "", str(s).lower())


def _strip_name(name):
    """«Audacity 3.7.7» -> audacity, «Python 3.14.5 (64-bit)» -> python."""
    s = re.sub(r"\(.*?\)|\[.*?\]", " ", name)
    s = re.sub(r"\b(v?\d+([._]\d+)*[a-z]?|x64|x86|64-bit|32-bit|amd64|win64|win32|beta|version)\b", " ", s,
               flags=re.I)
    return _norm(s)


def _strip_publisher(pub):
    s = str(pub or "")
    for _ in range(3):
        s = re.sub(r"[,.]?\s*\b(inc|ltd|llc|corp|corporation|co|gmbh|s\.?a|limited|aps|bvba|oy|sp\.?)\.?\s*$", "",
                   s.strip(), flags=re.I)
    return _norm(s)


def _aliases(app, all_apps):
    """Имена, по которым ищем остатки. Имя папки установки добавляем, только если оно связано
    с названием программы, не совпадает с издателем и не начало названия другой программы:
    иначе у After Effects (папка установки «Adobe») остатками стали бы папки Photoshop,
    а у TLauncher (папка «.minecraft») — миры Minecraft.
    -> (strong, weak): weak — имя без версии, общее с другой установленной программой
    (два Python, Visual C++ x64/x86): такие находки показываем без галочки."""
    out, weak = set(), set()
    n = _strip_name(app["name"])
    others = [_strip_name(a["name"]) for a in all_apps if a["id"] != app["id"]]
    if len(n) >= 4:
        (weak if n in others else out).add(n)
    if len(_norm(app["name"])) >= 4:
        out.add(_norm(app["name"]))  # полное имя с версией: папка «Adobe After Effects 2025»
    pub = _strip_publisher(app["publisher"])
    for d in (app["location"], app["exe_dir"]):
        if not d:
            continue
        b = _norm(Path(d).name)
        related = n and (b in n or n in b)
        shared = b == pub or any(o != n and o.startswith(b) for o in others)
        if len(b) >= 4 and b not in GENERIC and related and not shared and b not in weak:
            out.add(b)
    return out, weak - out


# ---------- список программ ----------

KEEP_PROGRAMS = [
    (r"visual c\+\+|redistributable|\.net|directx|runtime|webview", "библиотека: нужна другим программам"),
    (r"graphics driver|hd audio driver|physx|realtek|chipset|пакет драйверов|driver", "драйвер"),
    (r"vanguard|anti-cheat|faceit ac", "античит: без него не запустятся игры"),
    (r"python|git\b|java", "среда разработки: могут зависеть другие программы"),
]


def _hint(name):
    low = name.lower()
    for pattern, text in KEEP_PROGRAMS:
        if re.search(pattern, low):
            return "keep", tr(text)
    return None, ""


def _clean_path(s):
    s = str(s or "").strip().strip('"').strip()
    return os.path.normpath(s) if s else ""


def list_programs():
    """Установленные программы из разделов Uninstall. Обновления и системные компоненты скрыты."""
    apps, seen = [], set()
    for hive, base in (("HKLM", UNINSTALL), ("HKLM", UNINSTALL32), ("HKCU", UNINSTALL)):
        try:
            k = winreg.OpenKey(engine.HIVES[hive], base, 0, winreg.KEY_READ | winreg.KEY_WOW64_64KEY)
        except FileNotFoundError:
            continue
        with k:
            subs, i = [], 0
            while True:
                try:
                    subs.append(winreg.EnumKey(k, i))
                except OSError:
                    break
                i += 1
        for sub in subs:
            v = startup._values(hive, base + "\\" + sub)
            name = str(v.get("DisplayName") or "").strip()
            if (not name or v.get("SystemComponent") == 1 or v.get("ParentKeyName")
                    or str(v.get("ReleaseType", "")).lower() in ("update", "hotfix", "security update")
                    or re.search(r"\bKB\d{6,}\b", name)):
                continue
            version = str(v.get("DisplayVersion") or "")
            if (name.lower(), version) in seen:
                continue
            seen.add((name.lower(), version))
            uninstall = str(v.get("UninstallString") or v.get("QuietUninstallString") or "").strip()
            msi = v.get("WindowsInstaller") == 1 and re.fullmatch(r"\{[0-9A-Fa-f-]{36}\}", sub) is not None
            icon = _clean_path(str(v.get("DisplayIcon") or "").split(",")[0])
            exe = icon if icon.lower().endswith(".exe") else _clean_path(startup._exe(uninstall)) if uninstall else ""
            exe_dir = os.path.dirname(exe) if exe and "msiexec" not in exe.lower() else ""
            date = str(v.get("InstallDate") or "")
            size = v.get("EstimatedSize")
            hint, hint_text = _hint(name)
            apps.append({
                "id": f"{hive}:{base}\\{sub}", "name": name, "publisher": str(v.get("Publisher") or "").strip(),
                "version": version, "size": size * 1024 if isinstance(size, int) else None,
                "date": f"{date[6:8]}.{date[4:6]}.{date[:4]}" if re.fullmatch(r"\d{8}", date) else "",
                "location": _clean_path(v.get("InstallLocation")), "exe_dir": exe_dir,
                "uninstall": uninstall, "msi": msi, "msi_code": sub if msi else "",
                "hive": hive, "key_path": base + "\\" + sub, "hint": hint, "hint_text": hint_text,
            })
    apps.sort(key=lambda a: a["name"].lower())
    return apps


def still_installed(app):
    return startup._values(app["hive"], app["key_path"]) != {}


def run_uninstaller(app):
    """Запуск деинсталлятора программы (с её обычным окном). -> Popen."""
    if app["msi"]:
        return subprocess.Popen(["msiexec.exe", "/x", app["msi_code"]])
    if not app["uninstall"]:
        raise RuntimeError(tr("у программы не указан деинсталлятор"))
    return subprocess.Popen(os.path.expandvars(app["uninstall"]))  # строка: CreateProcess разберёт кавычки сам


# ---------- поиск остатков ----------

def _inside(path, parent):
    return path == parent or path.startswith(parent + os.sep)


def _is_reparse(p):
    try:
        return bool(os.stat(p, follow_symlinks=False).st_file_attributes & cleaner.FILE_ATTRIBUTE_REPARSE_POINT)
    except OSError:
        return True


def _dir_size(p):
    return sum(size for _, size in cleaner._walk([p], 0)[0])


def scan_leftovers(app, all_apps):
    """-> (список остатков [{kind: dir|file|reg, path, size, checked, why}], программа ещё установлена)."""
    strong, weak = _aliases(app, all_apps)
    weak_note = " " + tr("(имя общее с другой установленной программой: проверь)")

    def match(name):
        """True — точно эта программа, False — общее имя (без галочки), None — не она."""
        n = _norm(name)
        return True if n in strong else False if n in weak else None

    pub = _strip_publisher(app["publisher"])
    forced = still_installed(app)
    # места других установленных программ: папку, внутри которой они лежат, не предлагаем
    occupied = {_key(d) for a in all_apps if a["id"] != app["id"]
                for d in (a["location"], a["exe_dir"]) if d}
    found = {}

    def add_dir(path, why, checked=True):
        k = _key(path)
        if (k in PROTECTED or _inside(k, _key(WINDIR)) or len(Path(k).parts) < 2 or k in found
                or not os.path.isdir(path) or _is_reparse(path)
                or any(_inside(o, k) for o in occupied)):
            return
        found[k] = {"kind": "dir", "path": str(Path(path)), "checked": checked, "why": why}

    def add_file(path, why, checked=True):
        k = _key(path)
        if k not in found and os.path.isfile(path):
            found[k] = {"kind": "file", "path": str(Path(path)), "checked": checked, "why": why}

    def add_match(path, m, why):
        """Папка или ярлык, совпавшие по имени (m из match)."""
        why = why if m else why + weak_note
        if path.is_dir():
            add_dir(path, why, m)
        elif path.suffix.lower() == ".lnk":
            add_file(path, why, m)

    # 1. папка установки и папка с exe
    for d, why in ((app["location"], tr("папка установки")), (app["exe_dir"], tr("папка программы"))):
        if not d or not os.path.isdir(d):
            continue
        name_ok = match(Path(d).name) is True
        in_profile = any(_inside(_key(d), r) for r in USER_ROOTS)
        if in_profile and not name_ok:
            continue  # например, .minecraft у TLauncher: там пользовательские данные
        if len(Path(d).parts) < 3 and not name_ok:
            continue  # C:\Something без совпадения имени: слишком рискованно
        add_dir(d, why if name_ok else why + " " + tr("(имя не совпадает с программой: проверь, что внутри)"), name_ok)

    # 2. папки и ярлыки по имени: База\Программа и База\Издатель\Программа
    for base in FOLDER_BASES:
        try:
            children = list(base.iterdir())
        except OSError:
            continue
        for c in children:
            is_lnk = c.suffix.lower() == ".lnk"
            m = match(c.stem if is_lnk else c.name)
            if m is not None:
                add_match(c, m, tr("ярлык в меню «Пуск»") if is_lnk else tr("по имени в {folder}").format(folder=base.name))
            elif c.is_dir() and pub and len(pub) >= 3 and _norm(c.name) == pub and not _is_reparse(c):
                try:
                    for gc in c.iterdir():
                        g_lnk = gc.suffix.lower() == ".lnk"
                        gm = match(gc.stem if g_lnk else gc.name)
                        if gm is not None:
                            add_match(gc, gm, tr("ярлык в меню «Пуск»") if g_lnk else f"{c.name}\\{gc.name}")
                except OSError:
                    pass
    for desk in DESKTOPS:
        if desk:
            for f in desk.glob("*.lnk"):
                m = match(f.stem)
                if m is not None:
                    add_match(f, m, tr("ярлык на рабочем столе"))

    items = list(found.values())
    # вложенные в уже найденные папки не показываем отдельно
    dirs = [_key(i["path"]) for i in items if i["kind"] == "dir"]
    items = [i for i in items if not any(_key(i["path"]) != d and _inside(_key(i["path"]), d) for d in dirs)]
    for i in items:
        i["size"] = _dir_size(i["path"]) if i["kind"] == "dir" else os.path.getsize(i["path"])

    # 3. реестр: Software\Программа и Software\Издатель\Программа
    regs = []
    for hive, base in (("HKCU", "Software"), ("HKLM", "SOFTWARE"), ("HKLM", "SOFTWARE\\WOW6432Node"),
                       ("HKCU", "Software\\WOW6432Node")):
        for sub in _subkeys(hive, base):
            n = _norm(sub)
            if n in REG_PROTECTED:
                continue
            m = match(sub)
            if m is not None:
                regs.append((hive, f"{base}\\{sub}", tr("раздел реестра программы") + ("" if m else weak_note), m))
            elif pub and len(pub) >= 3 and n == pub:
                for sub2 in _subkeys(hive, f"{base}\\{sub}"):
                    m2 = match(sub2)
                    if m2 is not None:
                        regs.append((hive, f"{base}\\{sub}\\{sub2}",
                                     tr("раздел реестра {key}").format(key=f"{sub}\\{sub2}") + ("" if m2 else weak_note), m2))
    if forced:
        regs.append((app["hive"], app["key_path"], tr("запись в списке установленных программ"), True))
    for hive, path, why, checked in regs:
        items.append({"kind": "reg", "path": f"{hive}\\{path}", "hive": hive, "key": path, "size": None,
                      "checked": checked, "why": why})

    if forced:  # программа не удалена: всё это и есть программа, решать только пользователю
        for i in items:
            i["checked"] = False
    return items, forced


def _subkeys(hive, path):
    try:
        with winreg.OpenKey(engine.HIVES[hive], path, 0, winreg.KEY_READ | winreg.KEY_WOW64_64KEY) as k:
            out, i = [], 0
            while True:
                try:
                    out.append(winreg.EnumKey(k, i))
                except OSError:
                    return out
                i += 1
    except OSError:
        return []


# ---------- удаление остатков ----------

class _SHFILEOPSTRUCTW(ctypes.Structure):
    _fields_ = [("hwnd", wintypes.HWND), ("wFunc", wintypes.UINT), ("pFrom", wintypes.LPCWSTR),
                ("pTo", wintypes.LPCWSTR), ("fFlags", ctypes.c_ushort), ("fAnyOperationsAborted", wintypes.BOOL),
                ("hNameMappings", ctypes.c_void_p), ("lpszProgressTitle", wintypes.LPCWSTR)]
    if ctypes.sizeof(ctypes.c_void_p) == 4:
        _pack_ = 1  # в 32-битной Windows структура упакована


def _recycle(path):
    """В корзину. FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI."""
    op = _SHFILEOPSTRUCTW(None, 3, path + "\0", None, 0x40 | 0x10 | 0x4 | 0x400, False, None, None)
    rc = ctypes.windll.shell32.SHFileOperationW(ctypes.byref(op))
    if rc != 0 or op.fAnyOperationsAborted or os.path.exists(path):
        raise OSError(tr("не удалось удалить (код {rc}), возможно, файл занят").format(rc=rc))


def _export_reg(keys, name):
    """Все разделы в один .reg (UTF-16, как пишет reg.exe). -> путь к файлу."""
    REG_BACKUP_DIR.mkdir(parents=True, exist_ok=True)
    safe = re.sub(r"[^\w\-]+", "_", name)[:40]
    out = REG_BACKUP_DIR / f"{datetime.now():%Y-%m-%d_%H-%M-%S}_{safe}.reg"
    parts = []
    for i, key in enumerate(keys):
        tmp = out.with_suffix(f".{i}.tmp")
        rc, msg = engine.run(["reg", "export", key, str(tmp), "/y", "/reg:64"])
        if rc != 0:
            raise RuntimeError(tr("резервная копия {key} не создана: {err}").format(key=key, err=msg.strip()))
        text = tmp.read_text(encoding="utf-16")
        tmp.unlink()
        parts.append(text if i == 0 else text.split("\n", 1)[1])  # заголовок только один раз
    out.write_text("".join(parts), encoding="utf-16")
    return out


def remove_leftovers(items, app_name):
    """-> {'removed', 'freed', 'errors', 'backup'}."""
    removed, freed, errors, backup = 0, 0, [], None
    regs = [i for i in items if i["kind"] == "reg"]
    if regs:
        backup = _export_reg([i["path"] for i in regs], app_name)  # без копии реестр не трогаем
    for i in items:
        try:
            if i["kind"] == "reg":
                rc, msg = engine.run(["reg", "delete", i["path"], "/f", "/reg:64"])
                if rc != 0:
                    raise RuntimeError(msg.strip())
            else:
                _recycle(i["path"])
                freed += i["size"] or 0
                parent = os.path.dirname(i["path"])
                if _key(parent) not in PROTECTED:
                    try:
                        os.rmdir(parent)  # пустая папка издателя
                    except OSError:
                        pass
            removed += 1
        except Exception as e:
            errors.append(f"{i['path']}: {e}")
    return {"removed": removed, "freed": freed, "errors": errors, "backup": backup}


def log_uninstall(name):
    return engine._log({"id": "uninstall:" + name, "name": tr("Удалена программа: {name}").format(name=name)}, "uninstall")


# ---------- встроенные приложения (UWP) ----------

STORE_HIDDEN = ("Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller",
                "Microsoft.Winget.Source", "Microsoft.SecHealthUI", "Microsoft.VCLibs", "Microsoft.NET",
                "Microsoft.UI.Xaml", "Microsoft.Services.Store", "Microsoft.MicrosoftEdge",
                "Microsoft.WinAppRuntime", "MicrosoftCorporationII.WinAppRuntime", "Microsoft.WindowsAppRuntime",
                "Microsoft.LanguageExperiencePack", "Microsoft.AAD.BrokerPlugin", "Microsoft.AccountsControl",
                "Microsoft.LockApp", "Microsoft.Win32WebViewHost", "windows.immersivecontrolpanel",
                "Microsoft.Windows.ShellExperienceHost", "Microsoft.Windows.StartMenuExperienceHost")
STORE_NAMES = {
    "Microsoft.BingWeather": ("Погода (MSN)", "remove"),
    "Microsoft.BingNews": ("Новости (MSN)", "remove"),
    "Microsoft.BingSearch": ("Поиск Bing", "remove"),
    "Microsoft.GetHelp": ("Техническая поддержка", "remove"),
    "Microsoft.Getstarted": ("Советы", "remove"),
    "Microsoft.MicrosoftSolitaireCollection": ("Microsoft Solitaire Collection", "remove"),
    "Microsoft.MicrosoftOfficeHub": ("Microsoft 365 (реклама Office)", "remove"),
    "Microsoft.People": ("Люди", "remove"),
    "Microsoft.SkypeApp": ("Skype", "remove"),
    "Microsoft.WindowsFeedbackHub": ("Центр отзывов", "remove"),
    "Microsoft.WindowsMaps": ("Карты", "remove"),
    "Microsoft.ZuneVideo": ("Кино и ТВ", "remove"),
    "Microsoft.ZuneMusic": ("Медиаплеер / Groove Музыка", None),
    "Microsoft.YourPhone": ("Связь с телефоном", None),
    "Microsoft.MixedReality.Portal": ("Портал смешанной реальности", "remove"),
    "Microsoft.Microsoft3DViewer": ("Средство 3D-просмотра", "remove"),
    "Microsoft.MSPaint": ("Paint 3D", "remove"),
    "Microsoft.Office.OneNote": ("OneNote для Windows 10", "remove"),
    "Microsoft.Wallet": ("Кошелёк", "remove"),
    "Microsoft.Todos": ("Microsoft To Do", None),
    "Microsoft.PowerAutomateDesktop": ("Power Automate", "remove"),
    "Microsoft.MicrosoftPCManager": ("Microsoft PC Manager (ещё один «оптимизатор»)", "remove"),
    "Clipchamp.Clipchamp": ("Clipchamp", "remove"),
    "Microsoft.549981C3F5F10": ("Cortana", "remove"),
    "Microsoft.Copilot": ("Copilot", "remove"),
    "Microsoft.Windows.DevHome": ("Dev Home", "remove"),
    "MicrosoftWindows.Client.WebExperience": ("Виджеты", "remove"),
    "MicrosoftTeams": ("Microsoft Teams (личный)", "remove"),
    "MSTeams": ("Microsoft Teams", None),
    "Microsoft.OutlookForWindows": ("Outlook (новый)", None),
    "microsoft.windowscommunicationsapps": ("Почта и Календарь", None),
    "Microsoft.XboxApp": ("Xbox (старое приложение)", "remove"),
    "Microsoft.GamingApp": ("Xbox: нужен для Game Pass", None),
    "Microsoft.XboxGamingOverlay": ("Xbox Game Bar: запись игр, Win+G", None),
    "Microsoft.XboxGameOverlay": ("Xbox Game Bar (компонент)", None),
    "Microsoft.XboxSpeechToTextOverlay": ("Xbox: преобразование речи в текст", "remove"),
    "Microsoft.XboxIdentityProvider": ("Вход в Xbox: нужен играм с аккаунтом Microsoft", "keep"),
    "Microsoft.Xbox.TCUI": ("Xbox TCUI: нужен играм с аккаунтом Microsoft", "keep"),
    "Microsoft.WindowsAlarms": ("Часы и будильник", None),
    "Microsoft.WindowsSoundRecorder": ("Звукозапись", None),
    "Microsoft.WindowsCamera": ("Камера", "keep"),
    "Microsoft.WindowsCalculator": ("Калькулятор", "keep"),
    "Microsoft.Windows.Photos": ("Фотографии: стандартный просмотр картинок", "keep"),
    "Microsoft.ScreenSketch": ("Ножницы (Win+Shift+S)", "keep"),
    "Microsoft.WindowsNotepad": ("Блокнот", "keep"),
    "Microsoft.Paint": ("Paint", "keep"),
    "Microsoft.WindowsTerminal": ("Терминал", "keep"),
    "Microsoft.MicrosoftStickyNotes": ("Записки", None),
    "Microsoft.Windows.Ai.Copilot.Provider": ("Copilot (компонент)", "remove"),
    "MicrosoftCorporationII.QuickAssist": ("Быстрая помощь", None),
}
STORE_PREFIX = [("king.com.", "Игра от King (Candy Crush и т.п.)", "remove"),
                ("Disney.", "Disney+", "remove"), ("Facebook.", "Facebook", "remove"),
                ("BytedancePte.Ltd.TikTok", "TikTok", "remove"), ("AmazonVideo.", "Prime Video", "remove"),
                ("4DF9E0F8.Netflix", "Netflix", "remove"), ("SpotifyAB.", "Spotify", None)]
DRIVER_VENDORS = ("nvidia", "realtek", "amd", "intel", "dolby", "synaptics", "elan", "waves", "dts")


def list_store_apps():
    ps = ("[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-AppxPackage | Where-Object { -not $_.IsFramework "
          "-and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' } | "
          "Select-Object Name, PackageFullName, Version | ConvertTo-Json -Compress")
    rc, out = engine.run(["powershell", "-NoProfile", "-Command", ps], encoding="utf-8")
    if rc != 0:
        raise RuntimeError(out.strip().splitlines()[0] if out.strip() else tr("код {rc}").format(rc=rc))
    data = json.loads(out) if out.strip() else []
    if isinstance(data, dict):
        data = [data]
    apps = []
    for d in data:
        name = d["Name"]
        if any(name.lower().startswith(h.lower()) for h in STORE_HIDDEN):
            continue
        title, hint = STORE_NAMES.get(name, (None, None))
        if title is None:
            title, hint = next(((t, h) for p, t, h in STORE_PREFIX if name.startswith(p)), (None, None))
        if title is None:
            title = name.split(".", 1)[-1] if "." in name else name
        else:
            title = tr(title)
        low = name.lower()
        hint_text = ""
        if "extension" in low:
            hint, hint_text = "keep", tr("кодек: без него не откроются некоторые фото и видео")
        elif any(v in low for v in DRIVER_VENDORS):
            hint, hint_text = "keep", tr("панель управления драйвера")
        apps.append({"id": name, "name": title, "package": name, "version": d.get("Version", ""),
                     "hint": hint, "hint_text": hint_text})
    apps.sort(key=lambda a: (a["hint"] != "remove", a["name"].lower()))
    return apps


def remove_store_app(package):
    if not re.fullmatch(r"[A-Za-z0-9._-]+", package):
        raise ValueError(tr("странное имя пакета"))
    ps = (f"$n='{package}'; $ErrorActionPreference='Stop'; "
          "try { Get-AppxPackage -AllUsers -Name $n | Remove-AppxPackage -AllUsers } "
          "catch { Get-AppxPackage -Name $n | Remove-AppxPackage }; "
          "Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq $n | "
          "ForEach-Object { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName | Out-Null }; "
          "if (Get-AppxPackage -Name $n) { throw 'приложение осталось установленным' }")
    rc, out = engine.run(["powershell", "-NoProfile", "-Command", ps])
    if rc != 0:
        raise RuntimeError(out.strip().splitlines()[-1] if out.strip() else tr("код {rc}").format(rc=rc))
    engine._log({"id": "store:" + package, "name": tr("Удалено приложение: {name}").format(name=package)}, "uninstall")
