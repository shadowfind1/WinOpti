"""winopt/startup.py: автозагрузка.

Источники: разделы реестра Run, папки «Автозагрузка» и сторонние задачи планировщика,
запускающиеся при входе или загрузке. Записи реестра и папок не удаляем, а помечаем
выключенными в StartupApproved, как Диспетчер задач: всё обратимо и совпадает с ним.
Задачи включаем и выключаем как обычно (schtasks).
"""
import json
import os
import re
import struct
import time
import winreg
import xml.etree.ElementTree as ET
from pathlib import Path

import engine
from i18n import tr

RUN = "Software\\Microsoft\\Windows\\CurrentVersion\\Run"
RUN32 = "Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Run"
APPROVED = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\"

# (источник, улей, раздел Run, подраздел StartupApproved, подпись)
REG_SOURCES = [
    ("hkcu_run", "HKCU", RUN, "Run", "реестр, текущий пользователь"),
    ("hklm_run", "HKLM", RUN, "Run", "реестр, все пользователи"),
    ("hklm_run32", "HKLM", RUN32, "Run32", "реестр, все пользователи (32-бит)"),
]
FOLDER_SOURCES = [
    ("user_folder", "HKCU", Path(os.environ.get("APPDATA", "")) / "Microsoft/Windows/Start Menu/Programs/Startup",
     "папка «Автозагрузка», текущий пользователь"),
    ("common_folder", "HKLM", Path(os.environ.get("ProgramData", "")) / "Microsoft/Windows/Start Menu/Programs/StartUp",
     "папка «Автозагрузка», все пользователи"),
]

# Подсказки: (подстроки в имени или пути, текст). KEEP проверяем первым.
KEEP = [
    (("securityhealth",), "значок «Безопасности Windows»"),
    (("vanguard", "vgtray"), "античит Riot: без него не запустятся Valorant и League of Legends"),
    (("faceit ac", "faceitac"), "античит FACEIT"),
    (("realtek", "rtkaud", "ravcpl", "rthdvcpl"), "звуковой драйвер"),
    (("synaptics", "syntp", "elan", "etdctrl"), "драйвер тачпада"),
    (("igfx", "intel"), "драйвер Intel"),
    (("avp.exe", "kaspersky", "eset", "egui", "avast", "avgui", "drweb", "360tray", "defender"), "антивирус"),
    (("ctfmon",), "языковая панель и ввод текста"),
    (("lghub", "logitech", "razer", "steelseries", "corsair", "icue", "bloody"),
     "ПО мыши или клавиатуры: настройки DPI, макросы и подсветка"),
]
DISABLE = [
    (("steam",), "лаунчер, сам запустится при открытии игры"),
    (("epicgames", "epic games"), "лаунчер Epic Games"),
    (("riotclient",), "лаунчер Riot"),
    (("eadesktop", "ea desktop", "origin"), "лаунчер EA"),
    (("battle.net",), "лаунчер Battle.net"),
    (("ubisoft", "upc.exe"), "лаунчер Ubisoft"),
    (("roblox",), "лаунчер Roblox"),
    (("faceit",), "клиент FACEIT: запусти его перед игрой"),
    (("discord",), "мессенджер: оставь, если нужен сразу после входа"),
    (("telegram", "viber", "whatsapp", "skype", "teams", "zoom"), "мессенджер: оставь, если нужен сразу после входа"),
    (("spotify",), "музыка, запускается за секунду вручную"),
    (("onedrive",), "облако: выключи, если не синхронизируешь файлы"),
    (("autolaunch", "edgeupdate", "browser.exe", "opera"), "фоновый запуск браузера ради «быстрого старта»"),
    (("googleupdate", "google update"), "обновления Google: проверятся сами при запуске Chrome"),
    (("adobe", "acrotray"), "служебные программы и обновления Adobe"),
    (("jusched", "java update"), "проверка обновлений Java"),
    (("ituneshelper",), "помощник iTunes"),
    (("ccleaner",), "фоновый мониторинг CCleaner"),
    (("utorrent", "qbittorrent", "bittorrent"), "торрент-клиент"),
]


def _hint(text):
    text = text.lower()
    for keys, msg in KEEP:
        if any(k in text for k in keys):
            return "keep", tr(msg)
    for keys, msg in DISABLE:
        if any(k in text for k in keys):
            return "disable", tr(msg)
    return None, ""


# ---------- реестр ----------

def _values(hive, path):
    """-> {имя: значение} или {}, если раздела нет."""
    out = {}
    try:
        with winreg.OpenKey(engine.HIVES[hive], path, 0, winreg.KEY_READ | winreg.KEY_WOW64_64KEY) as k:
            i = 0
            while True:
                try:
                    name, value, _ = winreg.EnumValue(k, i)
                except OSError:
                    break
                out[name] = value
                i += 1
    except FileNotFoundError:
        pass
    return out


def _approved_enabled(data):
    """StartupApproved: первый байт чётный (02, 06) = включено, нечётный (03, 07) = выключено."""
    return not (isinstance(data, bytes) and data and data[0] & 1)


def _approved_bytes(enable):
    if enable:
        return b"\x02" + b"\x00" * 11
    filetime = int((time.time() + 11644473600) * 10 ** 7)  # когда выключено, как у Диспетчера задач
    return b"\x03\x00\x00\x00" + struct.pack("<Q", filetime)


def _exe(cmd):
    """Путь к программе из командной строки."""
    cmd = os.path.expandvars(str(cmd).strip())
    if cmd.startswith('"'):
        end = cmd.find('"', 1)
        return cmd[1:end] if end > 0 else cmd[1:]
    i = cmd.lower().find(".exe")
    return cmd[:i + 4] if i >= 0 else cmd.split(" ")[0]


# ---------- задачи планировщика ----------

def _tasks():
    """Сторонние (не \\Microsoft\\) задачи с триггером при входе или загрузке."""
    ns = engine.TASK_NS
    items = []
    root_dir = engine.TASKS_DIR
    for dirpath, dirnames, filenames in os.walk(root_dir):
        if Path(dirpath) == root_dir:
            dirnames[:] = [d for d in dirnames if d.lower() != "microsoft"]
        for fn in filenames:
            f = Path(dirpath) / fn
            try:
                root = ET.fromstring(f.read_bytes())
            except (OSError, ET.ParseError):
                continue
            triggers = root.find(f"{ns}Triggers")
            if triggers is None or not any(t.tag in (f"{ns}LogonTrigger", f"{ns}BootTrigger") for t in triggers):
                continue
            exec_ = root.find(f"{ns}Actions/{ns}Exec")
            command = ""
            if exec_ is not None:
                command = (exec_.findtext(f"{ns}Command") or "").strip()
                args = (exec_.findtext(f"{ns}Arguments") or "").strip()
                command = f'"{command.strip(chr(34))}" {args}'.strip() if command else ""
            enabled = root.find(f"{ns}Settings/{ns}Enabled")
            path = "\\" + str(f.relative_to(root_dir))
            items.append({"id": "task:" + path, "source": "task", "name": fn, "command": command,
                          "exe": _exe(command) if command else "", "where": tr("задача планировщика"),
                          "enabled": not (enabled is not None and enabled.text.strip().lower() == "false"),
                          "admin": True, "task_path": path})
    return items


# ---------- издатель и наличие файла (одним вызовом PowerShell) ----------

def _file_info(paths):
    paths = [p for p in dict.fromkeys(paths) if p]
    if not paths:
        return {}
    arr = ",".join("'" + p.replace("'", "''") + "'" for p in paths)
    ps = ("[Console]::OutputEncoding=[Text.Encoding]::UTF8; $r=@{}; $sh=New-Object -ComObject WScript.Shell; "
          f"foreach($p in @({arr})){{ try {{ $t=$p; if ($p -like '*.lnk') {{ $t=$sh.CreateShortcut($p).TargetPath }}; "
          "if (-not $t) { $r[$p]=@{exists=$true; company=''; target=''}; continue }; "
          "$i=Get-Item -LiteralPath $t -ErrorAction Stop; "
          "$r[$p]=@{exists=$true; company=[string]$i.VersionInfo.CompanyName; target=$t} } "
          "catch { $r[$p]=@{exists=$false; company=''; target=''} } }; $r | ConvertTo-Json -Compress -Depth 3")
    rc, out = engine.run(["powershell", "-NoProfile", "-Command", ps], encoding="utf-8")
    try:
        return json.loads(out) if rc == 0 and out.strip() else {}
    except ValueError:
        return {}


# ---------- публичный API ----------

def list_items():
    """-> (список записей, текст предупреждения или None). Включённые сверху."""
    items, warning = [], None
    for src, hive, path, approved, where in REG_SOURCES:
        flags = _values(hive, APPROVED + approved)
        for name, cmd in _values(hive, path).items():
            if not name:
                continue
            pretty = re.sub(r"_[0-9A-Fa-f]{16,}$", "", name)  # BrowserAutoLaunch_9A74EE… -> BrowserAutoLaunch
            items.append({"id": f"{src}:{name}", "source": src, "name": pretty, "command": str(cmd),
                          "exe": _exe(cmd), "where": tr(where), "enabled": _approved_enabled(flags.get(name)),
                          "admin": hive == "HKLM", "hive": hive, "approved": APPROVED + approved,
                          "value_name": name})
    for src, hive, folder, where in FOLDER_SOURCES:
        flags = _values(hive, APPROVED + "StartupFolder")
        try:
            files = [f for f in folder.iterdir() if f.is_file() and f.name.lower() != "desktop.ini"]
        except OSError:
            files = []
        for f in files:
            items.append({"id": f"{src}:{f.name}", "source": src, "name": f.stem, "command": str(f),
                          "exe": str(f), "where": tr(where), "enabled": _approved_enabled(flags.get(f.name)),
                          "admin": hive == "HKLM", "hive": hive, "approved": APPROVED + "StartupFolder",
                          "value_name": f.name})
    try:
        items += _tasks()
    except OSError as e:
        warning = tr("задачи планировщика не прочитаны: {err}").format(err=e)

    info = _file_info([it["exe"] for it in items])
    for it in items:
        fi = info.get(it["exe"], {})
        it["company"] = (fi.get("company") or "").strip()
        it["missing"] = bool(info) and it["exe"] in info and not fi.get("exists", True)
        target = fi.get("target") or ""
        it["hint"], it["hint_text"] = _hint(" ".join((it["name"], it["exe"], target, it["company"])))
    items.sort(key=lambda it: (not it["enabled"], it["name"].lower()))
    return items, warning


def set_enabled(item, enable):
    if item["source"] == "task":
        engine._task_set(item["task_path"], enable)
    else:
        engine.reg_write(item["hive"], item["approved"], item.get("value_name", item["name"]),
                         "BINARY", _approved_bytes(enable))
    engine._log({"id": "startup:" + item["id"], "name": tr("Автозагрузка: {name}").format(name=item["name"])},
                "startup_on" if enable else "startup_off")


def is_enabled(item):
    """Перечитать состояние одной записи (после переключения)."""
    if item["source"] == "task":
        return engine._task_state(item["task_path"]) not in ("disabled", "missing")
    flags = _values(item["hive"], item["approved"])
    return _approved_enabled(flags.get(item.get("value_name", item["name"])))
