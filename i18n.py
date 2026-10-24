"""winopt/i18n.py: два языка интерфейса — русский (исходные строки прямо в коде) и английский.

  tr("Русский текст")            -> перевод из lang_en.EN, если выбран английский
  tr("{n} из {total}").format()  -> шаблоны переводим целиком, подставляем после
  loc(obj, "name")               -> obj["name_en"] для английского (твики, пресеты), иначе obj["name"]
Строки без перевода показываются по-русски: приложение не ломается, а проверка полноты
(разбор вызовов tr в коде) находит, что ещё не переведено.
Язык читается один раз при импорте; смена языка = перезапуск окна.
"""
import json
import os
from pathlib import Path

SETTINGS_FILE = Path(os.environ.get("APPDATA", ".")) / "winopt" / "settings.json"
LANGS = ("ru", "en")


def _load():
    try:
        lang = json.loads(SETTINGS_FILE.read_text(encoding="utf-8")).get("lang")
    except (FileNotFoundError, ValueError):
        lang = None
    return lang if lang in LANGS else "ru"


LANG = _load()
if LANG == "en":
    from lang_en import EN
else:
    EN = {}


def tr(text):
    return EN.get(text, text)


def loc(obj, key):
    """Поле объекта на текущем языке: name_en / description_en / after_en, если есть."""
    if LANG == "en" and obj.get(f"{key}_en"):
        return obj[f"{key}_en"]
    return obj.get(key)


def set_lang(lang):
    """Сохранить язык (применится после перезапуска окна)."""
    try:
        data = json.loads(SETTINGS_FILE.read_text(encoding="utf-8"))
    except (FileNotFoundError, ValueError):
        data = {}
    data["lang"] = lang
    SETTINGS_FILE.parent.mkdir(parents=True, exist_ok=True)
    SETTINGS_FILE.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
