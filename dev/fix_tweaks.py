"""Правка tweaks.json WinOpti: виджеты (новые сборки Windows 11) и реклама на экране блокировки."""
import json
import sys
from pathlib import Path

p = Path(sys.argv[1])
raw = p.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
data = json.loads(raw)
T = {t["id"]: t for t in data}
CDM = r"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
DSH = r"SOFTWARE\Policies\Microsoft\Dsh"


def reg(hive, path, name, value, default):
    return {"type": "registry", "hive": hive, "path": path, "name": name, "kind": "DWORD", "value": value, "default": default}


w = T["disable_widgets"]
w["refresh"] = ["widgets", "explorer"]
w["description"] = ("Отключает виджеты Windows 11 (новости, погода): кнопку на панели задач и саму панель, "
                    "в том числе в новых версиях Windows 11 (24H2 и новее).")
w["description_en"] = ("Turns off Windows 11 widgets (news, weather): the taskbar button and the widgets board itself, "
                       "including newer Windows 11 versions (24H2 and later).")
w["actions"] += [
    reg("HKLM", DSH, "DisableWidgetsBoard", 1, None),
    reg("HKLM", r"SOFTWARE\Microsoft\PolicyManager\default\NewsAndInterests\AllowNewsAndInterests", "value", 0, None),
]

a = T["disable_promo_app_installs"]
a["name"] = "Убрать рекламу Windows и автоустановку приложений"
a["name_en"] = "Remove Windows ads and promo app installs"
a["description"] = ("Windows перестаёт сама ставить приложения-«рекомендации» (игры, соцсети) и показывать рекламу, "
                    "советы и «интересные факты» на экране блокировки. В Windows 11 с экрана блокировки уходят и виджеты "
                    "с новостями. Уже установленные приложения остаются.")
a["description_en"] = ("Windows stops installing \"suggested\" apps (games, social networks) on its own and stops showing ads, "
                       "tips and \"fun facts\" on the lock screen. On Windows 11 the news widgets leave the lock screen too. "
                       "Already installed apps stay.")
a["after"] = "Экран блокировки — со следующей блокировки или перезагрузки"
a["after_en"] = "Lock screen: from the next lock or restart"
a["actions"] += [
    reg("HKCU", CDM, "RotatingLockScreenOverlayEnabled", 0, 1),
    reg("HKCU", CDM, "SubscribedContent-338387Enabled", 0, 1),
    reg("HKLM", DSH, "DisableWidgetsOnLockScreen", 1, None),
]

out = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
if crlf:
    out = out.replace("\n", "\r\n")
p.write_bytes(out.encode("utf-8"))
print("crlf", crlf, "ok")
