"""WinOpti 1.0.4: необязательные действия твика и надёжное отключение виджетов в Windows 11."""
import json
import sys
from pathlib import Path

root = Path(sys.argv[1])


def edit(path, pairs):
    p = root / path
    s = p.read_bytes().decode("utf-8")
    nl = "\r\n" if "\r\n" in s else "\n"
    s = s.replace("\r\n", "\n")
    for a, b in pairs:
        assert s.count(a) == 1, (path, a)
        s = s.replace(a, b)
    p.write_bytes(s.replace("\n", nl).encode("utf-8"))


edit("engine.py", [
    ('''def get_state(t):
    """'on' / 'off' / 'partial' по реальному состоянию системы."""
    flags = [HANDLERS[a["type"]]["detect"](a) for a in t["actions"]]''',
     '''def get_state(t):
    """'on' / 'off' / 'partial' по реальному состоянию системы. Необязательные действия (optional) не считаются:
    Windows может не дать их записать, а твик при этом работает."""
    flags = [HANDLERS[a["type"]]["detect"](a) for a in t["actions"] if not a.get("optional")]'''),
    ('''    for a in t["actions"]:
        HANDLERS[a["type"]]["apply"](a)
    return _join(_refresh(t), _log(t, "on"))''',
     '''    for a in t["actions"]:
        _run_action(a, "apply")
    return _join(_refresh(t), _log(t, "on"))'''),
    ('''    for a, snap in zip(t["actions"], snaps):
        HANDLERS[a["type"]]["restore"](a, snap)''',
     '''    for a, snap in zip(t["actions"], snaps):
        _run_action(a, "restore", snap)'''),
    ('''def _join(*warnings):''',
     '''def _run_action(a, step, *args):
    """Действие optional — запасной путь (например, ключ, который новые сборки Windows защищают):
    его сбой не ломает твик."""
    try:
        HANDLERS[a["type"]][step](a, *args)
    except OSError:
        if not a.get("optional"):
            raise


def _join(*warnings):'''),
])

p = root / "tweaks.json"
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
data = json.loads(raw)
w = next(t for t in data if t["id"] == "disable_widgets")
names = [a["name"] for a in w["actions"]]
assert names == ["AllowNewsAndInterests", "DisableWidgetsBoard", "value"], names
w["actions"][1]["optional"] = True
w["actions"][2]["optional"] = True
w["actions"].append({
    "type": "registry", "hive": "HKCU",
    "path": r"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
    "name": "TaskbarDa", "kind": "DWORD", "value": 0, "default": 1, "optional": True,
})
w["after"] = "Если кнопка виджетов не пропала сразу — перезагрузи ПК"
w["after_en"] = "If the widgets button is still there, restart the PC"
out = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
if crlf:
    out = out.replace("\n", "\r\n")
p.write_bytes(out.encode("utf-8"))
print("ok")
