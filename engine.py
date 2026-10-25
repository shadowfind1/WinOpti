"""winopt/engine.py: загрузка твиков, определение состояния, apply / revert.

Каждое действие (action) в tweaks.json имеет тип, а для типа есть четыре функции:
  detect(a)        -> bool   применено ли действие сейчас
  snapshot(a)      -> dict   запомнить исходное состояние (для отката)
  apply(a)                   применить
  restore(a, snap)           вернуть как было (snap может быть None, тогда берём default)

Поле твика "refresh" (список имён из REFRESHERS): после apply и revert текущие значения
из реестра сразу передаются Windows, чтобы эффект был без перезахода и перезагрузки.
"""
import ctypes
import json
import os
import re
import subprocess
import sys
import time
import winreg
import xml.etree.ElementTree as ET
from ctypes import wintypes
from datetime import datetime, timedelta
from pathlib import Path

from i18n import tr

TWEAKS_FILE = Path(__file__).with_name("tweaks.json")
DATA_DIR = Path(os.environ.get("APPDATA", ".")) / "winopt"
BACKUP_FILE = DATA_DIR / "backup.json"
HISTORY_FILE = DATA_DIR / "history.json"

HIVES = {"HKLM": winreg.HKEY_LOCAL_MACHINE, "HKCU": winreg.HKEY_CURRENT_USER}
KINDS = {"DWORD": winreg.REG_DWORD, "QWORD": winreg.REG_QWORD, "SZ": winreg.REG_SZ,
         "EXPAND_SZ": winreg.REG_EXPAND_SZ, "MULTI_SZ": winreg.REG_MULTI_SZ,
         "BINARY": winreg.REG_BINARY, "NONE": winreg.REG_NONE}
KINDS_BACK = {v: k for k, v in KINDS.items()}
SVC_KEY = "SYSTEM\\CurrentControlSet\\Services\\"
SVC_MODES = {2: "auto", 3: "demand", 4: "disabled"}
GUID_RE = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", re.I)
SCHEME_BALANCED = "381b4222-f694-41f0-9685-ff5bb260df2e"


# ---------- утилиты ----------

CREATE_NO_WINDOW = 0x08000000  # без мелькающих окон консоли у программы без консоли (exe, pythonw)


def run(cmd, encoding="oem"):
    """Запуск команды. Список = без shell, строка = через shell. -> (код, вывод)."""
    r = subprocess.run(cmd, capture_output=True, text=True, encoding=encoding,
                       errors="replace", shell=isinstance(cmd, str), creationflags=CREATE_NO_WINDOW)
    return r.returncode, (r.stdout or "") + (r.stderr or "")


def is_admin():
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def windows_version():
    return "11" if sys.getwindowsversion().build >= 22000 else "10"


def _session_user():
    """Пользователь, который сидит за ПК (владелец текущей сессии), или None."""
    wts = ctypes.windll.wtsapi32
    buf, size = wintypes.LPWSTR(), wintypes.DWORD()
    # WTS_CURRENT_SERVER_HANDLE = 0, WTS_CURRENT_SESSION = -1, WTSUserName = 5
    if not wts.WTSQuerySessionInformationW(None, wintypes.DWORD(0xFFFFFFFF), 5,
                                           ctypes.byref(buf), ctypes.byref(size)):
        return None
    try:
        return buf.value
    finally:
        wts.WTSFreeMemory(buf)


def _process_user():
    buf, size = ctypes.create_unicode_buffer(257), wintypes.DWORD(257)
    return buf.value if ctypes.windll.advapi32.GetUserNameW(buf, ctypes.byref(size)) else None


def other_user():
    """Имя пользователя сессии, если программа запущена от ДРУГОГО пользователя
    (в UAC ввели пароль другого админа). Тогда HKCU и APPDATA принадлежат не тому,
    кто сидит за ПК, и твики HKCU применятся не ему. Иначе None."""
    try:
        session, proc = _session_user(), _process_user()
    except Exception:
        return None
    if session and proc and session.lower() != proc.lower():
        return session
    return None


# ---------- реестр ----------

def reg_read(hive, path, name):
    try:
        with winreg.OpenKey(HIVES[hive], path, 0,
                            winreg.KEY_READ | winreg.KEY_WOW64_64KEY) as k:
            value, kind = winreg.QueryValueEx(k, name)
            return {"exists": True, "value": value, "kind": KINDS_BACK.get(kind, kind)}
    except FileNotFoundError:
        return {"exists": False}


def reg_write(hive, path, name, kind, value):
    """kind: имя из KINDS или число (редкий тип, сохранённый в бэкапе как есть)."""
    with winreg.CreateKeyEx(HIVES[hive], path, 0,
                            winreg.KEY_SET_VALUE | winreg.KEY_WOW64_64KEY) as k:
        winreg.SetValueEx(k, name, 0, KINDS.get(kind, kind), value)


def reg_delete(hive, path, name):
    try:
        with winreg.OpenKey(HIVES[hive], path, 0,
                            winreg.KEY_SET_VALUE | winreg.KEY_WOW64_64KEY) as k:
            winreg.DeleteValue(k, name)
    except FileNotFoundError:
        pass


def registry_detect(a):
    cur = reg_read(a["hive"], a["path"], a["name"])
    return cur["exists"] and cur["value"] == a["value"]


def registry_snapshot(a):
    snap = reg_read(a["hive"], a["path"], a["name"])
    if isinstance(snap.get("value"), bytes):  # REG_BINARY не ложится в JSON, храним hex
        snap["value"], snap["hex"] = snap["value"].hex(), True
    return snap


def registry_apply(a):
    reg_write(a["hive"], a["path"], a["name"], a["kind"], a["value"])


def registry_restore(a, snap):
    if snap is None:  # бэкапа нет, берём default из JSON (null = удалить значение)
        snap = {"exists": a.get("default") is not None,
                "value": a.get("default"), "kind": a["kind"]}
    if snap["exists"]:
        value = bytes.fromhex(snap["value"]) if snap.get("hex") else snap["value"]
        reg_write(a["hive"], a["path"], a["name"], snap["kind"], value)
    else:
        reg_delete(a["hive"], a["path"], a["name"])


# ---------- раздел реестра (важно само его наличие) ----------
# Пример: классическое контекстное меню Windows 11. path создаём со значением
# по умолчанию value; при откате, если раздела не было, удаляем path и всех
# родителей вверх до root включительно (только если они пустые).

def _key_exists(hive, path):
    try:
        winreg.CloseKey(winreg.OpenKey(HIVES[hive], path, 0, winreg.KEY_READ | winreg.KEY_WOW64_64KEY))
        return True
    except FileNotFoundError:
        return False


def regkey_detect(a):
    cur = reg_read(a["hive"], a["path"], "")
    return cur["exists"] and cur["value"] == a.get("value", "")


def regkey_snapshot(a):
    return {"existed": _key_exists(a["hive"], a["path"]), "default": reg_read(a["hive"], a["path"], "")}


def regkey_apply(a):
    reg_write(a["hive"], a["path"], "", "SZ", a.get("value", ""))


def regkey_restore(a, snap):
    if snap and snap["existed"]:
        registry_restore({**a, "name": "", "kind": "SZ"}, snap["default"])
        return
    path, root = a["path"], a.get("root", a["path"])
    while True:
        try:
            winreg.DeleteKeyEx(HIVES[a["hive"]], path, winreg.KEY_WOW64_64KEY)
        except OSError:
            break  # раздела нет или в нём что-то есть: дальше не трогаем
        if path.lower() == root.lower() or "\\" not in path:
            break
        path = path.rsplit("\\", 1)[0]


# ---------- службы ----------

def _svc_mode(name):
    """Тип запуска читаем из реестра: не зависит от языка системы."""
    cur = reg_read("HKLM", SVC_KEY + name, "Start")
    if not cur["exists"]:
        return None
    mode = SVC_MODES.get(cur["value"])
    if mode == "auto" and reg_read("HKLM", SVC_KEY + name, "DelayedAutostart").get("value") == 1:
        mode = "delayed-auto"
    return mode


def _svc_set(name, mode):
    rc, out = run(["sc.exe", "config", name, "start=", mode])
    if rc != 0:
        raise RuntimeError(f"sc config {name}: {out.strip()}")


def service_detect(a):
    mode = _svc_mode(a["name"])
    if mode is None:  # службы нет на этой системе: отключать нечего
        return a["start"] == "disabled"
    return mode == a["start"]


def service_snapshot(a):
    return {"mode": _svc_mode(a["name"])}


def service_apply(a):
    if _svc_mode(a["name"]) is None:
        if a["start"] == "disabled":
            return  # службы нет, считаем отключённой
        raise RuntimeError(tr("служба {name} не найдена").format(name=a["name"]))
    _svc_set(a["name"], a["start"])
    if a["start"] == "disabled":
        run(["sc.exe", "stop", a["name"]])  # если уже остановлена, ошибка не важна


def service_restore(a, snap):
    mode = snap["mode"] if snap else a.get("default")
    if mode:
        _svc_set(a["name"], mode)


# ---------- задачи планировщика ----------

TASKS_DIR = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32" / "Tasks"
TASK_NS = "{http://schemas.microsoft.com/windows/2004/02/mit/task}"


def _task_state_file(path):
    """Быстрый путь: XML-файл задачи (читается только с правами администратора).
    -> 'ready' / 'disabled' / 'missing', или None, если файл прочитать не удалось."""
    f = TASKS_DIR / path.lstrip("\\")
    try:
        root = ET.fromstring(f.read_bytes())
    except FileNotFoundError:
        return "missing"
    except (OSError, ET.ParseError):
        return None
    enabled = root.find(f"{TASK_NS}Settings/{TASK_NS}Enabled")
    return "disabled" if enabled is not None and enabled.text.strip().lower() == "false" else "ready"


def _task_state(path):
    """'ready' / 'disabled' / 'running' / …, 'missing' если задачи нет, None если не удалось прочитать."""
    state = _task_state_file(path)
    if state is not None:
        return state
    folder, name = path.rsplit("\\", 1)
    ps = (f"try {{ (Get-ScheduledTask -TaskPath '{folder}\\' -TaskName '{name}' -ErrorAction Stop).State }} "
          "catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { 'missing' } else { throw } }")
    rc, out = run(["powershell", "-NoProfile", "-Command", ps])
    return out.strip().lower() if rc == 0 else None


def _task_set(path, enable):
    rc, out = run(["schtasks.exe", "/Change", "/TN", path, "/Enable" if enable else "/Disable"])
    if rc != 0:
        raise RuntimeError(f"schtasks {path}: {out.strip()}")


def task_detect(a):
    return _task_state(a["path"]) in ("disabled", "missing")  # нет задачи: выключать нечего


def task_snapshot(a):
    return {"state": _task_state(a["path"])}


def task_apply(a):
    if _task_state(a["path"]) != "missing":
        _task_set(a["path"], False)


def task_restore(a, snap):
    state = snap["state"] if snap else "ready"
    # была включена: включаем; была выключена, отсутствовала или не прочиталась: не трогаем
    if state and state not in ("disabled", "missing") and _task_state(a["path"]) != "missing":
        _task_set(a["path"], True)


# ---------- произвольная команда (powercfg и т.п.) ----------

def _command(cmd):
    rc, out = run(cmd)
    if rc != 0:
        raise RuntimeError(f"{cmd}: {out.strip() or tr('код {rc}').format(rc=rc)}")


def command_detect(a):
    _, out = run(a["detect"])
    return a["detect_contains"].lower() in out.lower()


def command_snapshot(a):
    return None


def command_apply(a):
    _command(a["apply"])


def command_restore(a, snap):
    _command(a["revert"])


# ---------- схема питания ----------

def _active_scheme():
    rc, out = run(["powercfg", "/getactivescheme"])
    m = GUID_RE.search(out) if rc == 0 else None
    return m.group(0).lower() if m else None


def _set_scheme(guid):
    rc, out = run(["powercfg", "/setactive", guid])
    if rc != 0:
        raise RuntimeError(f"powercfg /setactive {guid}: {out.strip() or tr('код {rc}').format(rc=rc)}")


def _schemes():
    rc, out = run(["powercfg", "/list"])
    if rc != 0:
        raise RuntimeError(f"powercfg /list: {out.strip() or tr('код {rc}').format(rc=rc)}")
    return {g.lower() for g in GUID_RE.findall(out)}


def _targets(a):
    """GUID, которые считаются «включено»: своя копия схемы и сам шаблон (source),
    если он есть в системе открыто (например, в Windows 10/11 Pro for Workstations)."""
    return {a["scheme"].lower()} | ({a["source"].lower()} if a.get("source") else set())


def power_detect(a):
    return _active_scheme() in _targets(a)


def power_snapshot(a):
    return {"scheme": _active_scheme()}


def power_apply(a):
    """Если задан source, а схемы нет, создаём копию шаблона под фиксированным GUID scheme:
    повторное включение не плодит копии, а откат знает, какую схему удалить."""
    schemes = _schemes()
    guid = a["scheme"].lower()
    source = (a.get("source") or "").lower()
    if guid not in schemes:
        if source in schemes:
            guid = source
        elif source:
            rc, out = run(["powercfg", "-duplicatescheme", source, guid])
            if rc != 0:
                raise RuntimeError(tr("не удалось создать схему питания: {err}").format(
                    err=out.strip() or tr("код {rc}").format(rc=rc)))
        else:
            raise RuntimeError(tr("этой схемы питания нет на ПК (часто так на ноутбуках с Modern Standby)"))
    _set_scheme(guid)


def power_restore(a, snap):
    """Возвращаем схему, которая была до включения (если её уже удалили, ставим default),
    потом удаляем копию, созданную WinOpti."""
    fallback = a.get("default", SCHEME_BALANCED)
    guid = (snap or {}).get("scheme") or fallback
    if guid in _targets(a):  # до включения и так была эта схема
        guid = fallback
    try:
        _set_scheme(guid)
    except RuntimeError:
        if guid == fallback:
            raise
        _set_scheme(fallback)
    if a.get("source") and a["scheme"].lower() in _schemes():
        run(["powercfg", "-delete", a["scheme"]])  # не удалилась: не страшно, схема просто останется


HANDLERS = {
    "registry": dict(detect=registry_detect, snapshot=registry_snapshot,
                     apply=registry_apply, restore=registry_restore),
    "registry_key": dict(detect=regkey_detect, snapshot=regkey_snapshot,
                         apply=regkey_apply, restore=regkey_restore),
    "service": dict(detect=service_detect, snapshot=service_snapshot,
                    apply=service_apply, restore=service_restore),
    "task": dict(detect=task_detect, snapshot=task_snapshot,
                 apply=task_apply, restore=task_restore),
    "command": dict(detect=command_detect, snapshot=command_snapshot,
                    apply=command_apply, restore=command_restore),
    "power_scheme": dict(detect=power_detect, snapshot=power_snapshot,
                         apply=power_apply, restore=power_restore),
}


# ---------- применение без перезахода (refresh) ----------
# Каждая функция читает уже записанные значения из реестра и сообщает их Windows,
# поэтому одинаково работает и после apply, и после revert.

SPIF_SENDCHANGE = 0x02
SPI_SETMOUSE, SPI_SETANIMATION, SPI_SETMENUSHOWDELAY = 0x0004, 0x0049, 0x006B
SPI_SETKEYBOARDSPEED, SPI_SETKEYBOARDDELAY = 0x000B, 0x0017
SPI_GETFILTERKEYS, SPI_SETFILTERKEYS = 0x0032, 0x0033
SPI_GETTOGGLEKEYS, SPI_SETTOGGLEKEYS = 0x0034, 0x0035
SPI_GETSTICKYKEYS, SPI_SETSTICKYKEYS = 0x003A, 0x003B
HWND_BROADCAST, WM_SETTINGCHANGE, SMTO_ABORTIFHUNG = 0xFFFF, 0x001A, 0x0002


class _ANIMATIONINFO(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.UINT), ("iMinAnimate", ctypes.c_int)]


class _FLAGS(ctypes.Structure):  # STICKYKEYS и TOGGLEKEYS
    _fields_ = [("cbSize", wintypes.UINT), ("dwFlags", wintypes.DWORD)]


class _FILTERKEYS(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.UINT), ("dwFlags", wintypes.DWORD), ("iWaitMSec", wintypes.DWORD),
                ("iDelayMSec", wintypes.DWORD), ("iRepeatMSec", wintypes.DWORD),
                ("iBounceMSec", wintypes.DWORD)]


def _spi(action, ui=0, pv=None):
    if not ctypes.windll.user32.SystemParametersInfoW(action, ui, pv, SPIF_SENDCHANGE):
        raise ctypes.WinError()


def _hkcu_int(path, name):
    cur = reg_read("HKCU", path, name)
    return int(cur["value"]) if cur["exists"] else None


def _broadcast(area):
    """WM_SETTINGCHANGE всем окнам: «перечитайте настройку area»."""
    send = ctypes.windll.user32.SendMessageTimeoutW
    send.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPCWSTR,
                     wintypes.UINT, wintypes.UINT, ctypes.POINTER(ctypes.c_size_t)]
    send(HWND_BROADCAST, WM_SETTINGCHANGE, 0, area, SMTO_ABORTIFHUNG, 2000,
         ctypes.byref(ctypes.c_size_t()))


def refresh_animation():
    """Сворачивание/разворачивание. В Windows это же отключает анимацию открытия и закрытия окон."""
    value = _hkcu_int("Control Panel\\Desktop\\WindowMetrics", "MinAnimate")
    if value is not None:
        info = _ANIMATIONINFO(ctypes.sizeof(_ANIMATIONINFO), value)
        _spi(SPI_SETANIMATION, ctypes.sizeof(info), ctypes.byref(info))


def refresh_mouse():
    vals = [_hkcu_int("Control Panel\\Mouse", n) for n in ("MouseThreshold1", "MouseThreshold2", "MouseSpeed")]
    if None not in vals:
        _spi(SPI_SETMOUSE, 0, (ctypes.c_int * 3)(*vals))


def refresh_keyboard():
    """Задержка перед автоповтором (0-3, шаг ~250 мс) и скорость повтора (0-31)."""
    delay = _hkcu_int("Control Panel\\Keyboard", "KeyboardDelay")
    speed = _hkcu_int("Control Panel\\Keyboard", "KeyboardSpeed")
    if delay is not None:
        _spi(SPI_SETKEYBOARDDELAY, delay)
    if speed is not None:
        _spi(SPI_SETKEYBOARDSPEED, speed)


def refresh_accessibility():
    """Залипание, фильтрация и озвучивание переключения клавиш: флаги из реестра."""
    base = "Control Panel\\Accessibility\\"
    for key, struct, get, set_ in (("StickyKeys", _FLAGS, SPI_GETSTICKYKEYS, SPI_SETSTICKYKEYS),
                                   ("Keyboard Response", _FILTERKEYS, SPI_GETFILTERKEYS, SPI_SETFILTERKEYS),
                                   ("ToggleKeys", _FLAGS, SPI_GETTOGGLEKEYS, SPI_SETTOGGLEKEYS)):
        flags = _hkcu_int(base + key, "Flags")
        if flags is None:
            continue
        s = struct()
        s.cbSize = ctypes.sizeof(s)
        _spi(get, s.cbSize, ctypes.byref(s))  # остальные поля оставляем как есть
        s.dwFlags = flags
        _spi(set_, s.cbSize, ctypes.byref(s))


def refresh_menu_delay():
    value = _hkcu_int("Control Panel\\Desktop", "MenuShowDelay")
    if value is not None:
        _spi(SPI_SETMENUSHOWDELAY, value)


def refresh_theme():
    _broadcast("ImmersiveColorSet")


def refresh_tray():
    _broadcast("TraySettings")


def refresh_explorer_view():
    """Расширения и скрытые файлы: так же, как кнопка «Применить» в «Параметрах папок»."""
    adv = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced"
    hidden, hide_ext = _hkcu_int(adv, "Hidden"), _hkcu_int(adv, "HideFileExt")
    mask = 0x1 | 0x2  # SSF_SHOWALLOBJECTS | SSF_SHOWEXTENSIONS
    state = (ctypes.c_uint32 * 16)()  # SHELLSTATE, нам нужны только первые два бита
    shell32 = ctypes.windll.shell32
    shell32.SHGetSetSettings(state, mask, False)
    bits = state[0] & ~0x3
    bits |= 0x1 if hidden == 1 else 0
    bits |= 0x2 if hide_ext == 0 else 0
    state[0] = bits
    shell32.SHGetSetSettings(state, mask, True)
    shell32.SHChangeNotify(0x08000000, 0, None, None)  # SHCNE_ASSOCCHANGED: обновить открытые окна


def _kill(*images):
    for image in images:
        run(["taskkill", "/f", "/im", image])  # процесса нет: ошибка не важна


def refresh_start_menu():
    _kill("StartMenuExperienceHost.exe")  # Windows сама запустит Пуск заново при открытии


def refresh_search():
    _kill("SearchApp.exe", "SearchUI.exe", "SearchHost.exe")


def _explorer_running():
    _, out = run(["tasklist", "/fi", "imagename eq explorer.exe", "/nh"])
    return "explorer.exe" in out.lower()


def refresh_explorer():
    """Перезапуск проводника. Запускать его из процесса администратора нельзя (панель задач
    станет «админской»), поэтому ждём автоперезапуск Windows, а если его нет, запускаем
    от имени пользователя сессии через планировщик."""
    _kill("explorer.exe")
    for _ in range(25):
        time.sleep(0.2)
        if _explorer_running():
            return
    user = _session_user()
    if not is_admin() or not user:
        subprocess.Popen(["explorer.exe"])
        return
    ps = (f"$p = New-ScheduledTaskPrincipal -UserId '{user}' -LogonType Interactive -RunLevel Limited; "
          "Register-ScheduledTask -TaskName 'WinOptiExplorer' -Principal $p -Force "
          "-Action (New-ScheduledTaskAction -Execute 'explorer.exe') | Out-Null; "
          "Start-ScheduledTask -TaskName 'WinOptiExplorer'; Start-Sleep -Seconds 2; "
          "Unregister-ScheduledTask -TaskName 'WinOptiExplorer' -Confirm:$false")
    rc, out = run(["powershell", "-NoProfile", "-Command", ps])
    if rc != 0:
        raise RuntimeError(tr("проводник не запустился, запусти explorer.exe вручную: {err}").format(
            err=out.strip()[:120]))


REFRESHERS = {
    "animation": refresh_animation, "mouse": refresh_mouse, "keyboard": refresh_keyboard,
    "accessibility": refresh_accessibility,
    "menu_delay": refresh_menu_delay, "theme": refresh_theme, "tray": refresh_tray,
    "explorer_view": refresh_explorer_view, "start_menu": refresh_start_menu,
    "search": refresh_search, "explorer": refresh_explorer,
}


def _refresh(t):
    """-> текст предупреждения или None. Сбой не откатывает твик: значения уже записаны,
    просто эффект появится после перезахода."""
    errors = []
    for name in t.get("refresh", []):
        try:
            REFRESHERS[name]()
        except Exception as e:
            errors.append(f"{name}: {e}")
    if errors:
        return tr("не удалось применить сразу, нужен перезаход ({list})").format(list="; ".join(errors))
    return None


# ---------- бэкап исходных значений ----------

def _load_backup():
    try:
        return json.loads(BACKUP_FILE.read_text(encoding="utf-8"))
    except (FileNotFoundError, ValueError):
        return {}


def _write_json(path, data):
    """Через временный файл: окно может читать файл одновременно с записью из фонового потока."""
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(".tmp")
    tmp.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(tmp, path)


def _save_backup(data):
    _write_json(BACKUP_FILE, data)


# ---------- публичный API ----------

def load_tweaks():
    data = json.loads(TWEAKS_FILE.read_text(encoding="utf-8"))
    return {t["id"]: t for t in data}


def is_hidden(t):
    """Твик убран из приложения (не ускоряет ПК): его не показываем и не включаем, но описание осталось,
    чтобы то, что уже включено, можно было откатить на вкладке «Восстановление»."""
    return bool(t.get("hidden"))


def is_supported(t):
    return windows_version() in t.get("windows", ["10", "11"])


def get_state(t):
    """'on' / 'off' / 'partial' по реальному состоянию системы."""
    flags = [HANDLERS[a["type"]]["detect"](a) for a in t["actions"]]
    if all(flags):
        return "on"
    return "partial" if any(flags) else "off"


def _load_history():
    try:
        return json.loads(HISTORY_FILE.read_text(encoding="utf-8"))
    except (FileNotFoundError, ValueError):
        return []


def _log(t, action):
    """Журнал изменений (on / off). Сбой записи журнала не ломает применение,
    но возвращается текстом предупреждения (None = записано)."""
    try:
        history = _load_history()
        history.append({"time": datetime.now().strftime("%Y-%m-%d %H:%M"),
                        "id": t["id"], "name": t["name"], "action": action})
        _write_json(HISTORY_FILE, history[-200:])
        return None
    except Exception as e:
        return tr("журнал не записан: {err}").format(err=e)


def apply_tweak(t):
    """-> текст предупреждения или None."""
    backup = _load_backup()
    snaps = backup.get(t["id"], [])
    # исходное состояние пишем один раз, повторный apply его не затирает;
    # если в tweaks.json дописали новые действия, снимаем только их
    if len(snaps) < len(t["actions"]):
        snaps += [HANDLERS[a["type"]]["snapshot"](a) for a in t["actions"][len(snaps):]]
        backup[t["id"]] = snaps
        _save_backup(backup)
    for a in t["actions"]:
        HANDLERS[a["type"]]["apply"](a)
    return _join(_refresh(t), _log(t, "on"))


def revert_tweak(t):
    """-> текст предупреждения или None."""
    backup = _load_backup()
    snaps = list(backup.get(t["id"], []))
    snaps += [None] * (len(t["actions"]) - len(snaps))  # для новых действий берём default
    for a, snap in zip(t["actions"], snaps):
        HANDLERS[a["type"]]["restore"](a, snap)
    if t["id"] in backup:
        del backup[t["id"]]
        _save_backup(backup)
    return _join(_refresh(t), _log(t, "off"))


def _join(*warnings):
    return "; ".join(w for w in warnings if w) or None


def forget_applied():
    """Очистить список изменений WinOpti: забыть сохранённые исходные значения. Сами твики остаются как есть.
    Выключить твик потом по-прежнему можно — revert_tweak вернёт значения Windows по умолчанию. -> сколько забыто."""
    backup = _load_backup()
    if backup:
        _save_backup({})
    return len(backup)


def get_applied(tweaks):
    """Твики, применённые через WinOpti и ещё не откаченные (у них есть бэкап). Новые сверху."""
    backup = _load_backup()
    history = _load_history()
    items = []
    for tid in backup:
        t = tweaks.get(tid)
        if t is None:
            continue
        when = next((e["time"] for e in reversed(history)
                     if e["id"] == tid and e["action"] == "on"), "")
        items.append({"id": tid, "name": t["name"], "time": when})
    items.sort(key=lambda x: x["time"], reverse=True)
    return items


def get_history(limit=20):
    """Последние записи журнала, новые сверху."""
    return list(reversed(_load_history()))[:limit]


SR_KEY = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore"
SR_FREQUENCY = "SystemRestorePointCreationFrequency"


def restore_protection_enabled():
    """Включена ли защита системы (хотя бы для одного диска): RPSessionInterval = 1."""
    return reg_read("HKLM", SR_KEY, "RPSessionInterval").get("value") == 1


def enable_restore_protection():
    """Включить защиту системы для системного диска."""
    drive = os.environ.get("SystemDrive", "C:") + "\\"
    rc, out = run(["powershell", "-NoProfile", "-Command",
                   f"$ErrorActionPreference='Stop'; Enable-ComputerRestore -Drive '{drive}'"])
    if rc != 0:
        raise RuntimeError(out.strip().splitlines()[0] if out.strip() else tr("код {rc}").format(rc=rc))


def _newest_restore_point():
    try:
        points = list_restore_points(1)
        return points[0]["n"] if points else None
    except Exception:
        return None


def create_restore_point_msg(desc="WinOpti"):
    """Точка восстановления. -> (успех, текст ошибки).
    Windows по умолчанию создаёт не больше одной точки в сутки, а лишние молча пропускает. На время
    создания снимаем это ограничение (SystemRestorePointCreationFrequency = 0) и возвращаем как было.
    Успех проверяем по списку точек: появилась новая — создана."""
    if not restore_protection_enabled():
        return False, tr("защита системы выключена: включи её на вкладке «Восстановление»")
    before = _newest_restore_point()
    old = reg_read("HKLM", SR_KEY, SR_FREQUENCY)
    try:
        reg_write("HKLM", SR_KEY, SR_FREQUENCY, "DWORD", 0)
        changed = True
    except OSError:
        changed = False  # нет прав: пробуем как есть
    try:
        ps = ("$ErrorActionPreference='Stop'; $WarningPreference='Stop'; "
              f"Checkpoint-Computer -Description '{desc}' -RestorePointType MODIFY_SETTINGS")
        rc, out = run(["powershell", "-NoProfile", "-Command", ps])
    finally:
        if changed:
            if old["exists"]:
                reg_write("HKLM", SR_KEY, SR_FREQUENCY, old["kind"], old["value"])
            else:
                reg_delete("HKLM", SR_KEY, SR_FREQUENCY)
    first = next((line.strip() for line in out.splitlines() if line.strip()), "")
    if rc != 0:
        return False, first
    after = _newest_restore_point()
    if after is not None and after == before:
        return False, tr("Windows не создала новую точку")
    return True, ""


def auto_restore_point_msg(desc="WinOpti", hours=24):
    """Точка перед включением твиков. Если свежая точка (моложе суток) уже есть, новую не создаём:
    создание точки — теневой снимок диска, и на это время (до минуты) вся Windows заметно тормозит."""
    try:
        points = list_restore_points(1)
        newest = datetime.strptime(points[0]["t"], "%Y-%m-%d %H:%M") if points else None
    except Exception:
        newest = None
    if newest and datetime.now() - newest < timedelta(hours=hours):
        return True, ""
    return create_restore_point_msg(desc)


def create_restore_point(desc="WinOpti"):
    return create_restore_point_msg(desc)[0]


def list_restore_points(limit=10):
    """Точки восстановления Windows: [{'n': номер, 'd': описание, 't': 'ГГГГ-ММ-ДД ЧЧ:ММ'}], новые сверху."""
    ps = ("[Console]::OutputEncoding=[Text.Encoding]::UTF8; "
          "Get-ComputerRestorePoint | ForEach-Object { [pscustomobject]@{ n=$_.SequenceNumber; "
          "d=$_.Description; t=$_.ConvertToDateTime($_.CreationTime).ToString('yyyy-MM-dd HH:mm') } } "
          "| ConvertTo-Json -Compress")
    rc, out = run(["powershell", "-NoProfile", "-Command", ps], encoding="utf-8")
    out = out.strip()
    if rc != 0:
        raise RuntimeError(out.splitlines()[0] if out else tr("не удалось получить список"))
    if not out:
        return []
    data = json.loads(out)
    if isinstance(data, dict):
        data = [data]
    data.sort(key=lambda p: p["n"], reverse=True)
    return data[:limit]


def delete_restore_points():
    """Удалить все точки восстановления Windows. -> (сколько удалено, [ошибки]).
    SRRemoveRestorePoint удаляет именно точки восстановления; другие теневые копии диска (например, у программ
    резервного копирования) не трогаем — поэтому не vssadmin delete shadows. Нужны права администратора."""
    points = list_restore_points(limit=10 ** 6)
    remove = ctypes.windll.srclient.SRRemoveRestorePoint
    remove.argtypes = [wintypes.DWORD]
    remove.restype = wintypes.DWORD
    deleted, errors = 0, []
    for p in points:
        rc = remove(int(p["n"]))
        if rc == 0:
            deleted += 1
        else:
            errors.append(f"{p['t']}: {ctypes.FormatError(rc).strip()}")
    return deleted, errors


def open_system_restore():
    """Окно «Восстановление системы»: откат всей Windows к выбранной точке."""
    subprocess.Popen(["rstrui.exe"])
