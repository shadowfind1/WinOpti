"""winopt/main.py: консольный прототип.

  python main.py list                  состояние всех твиков
  python main.py on  <id> [--no-restore]   применить
  python main.py off <id>              откатить
  python main.py scan                  сколько места можно освободить
  python main.py clean <id ...|all>    очистить
(on/off и системные пункты очистки нужно запускать от имени администратора)
"""
import sys

import cleaner
import engine


def main(argv):
    tweaks = engine.load_tweaks()
    cmd = argv[0] if argv else "list"

    if cmd == "list":
        print(f"Windows {engine.windows_version()}\n")
        for t in tweaks.values():
            if engine.is_hidden(t):
                continue
            state = engine.get_state(t) if engine.is_supported(t) else "n/a"
            print(f"[{state:7}] {t['id']:24} {t['name']}  (риск: {t['risk']})")
        return 0

    if cmd == "scan":
        for it in cleaner.ITEMS:
            if it.admin and not engine.is_admin():
                size = "нужен админ"
            else:
                try:
                    size = cleaner.fmt_size(it.scan())
                except Exception as e:
                    size = f"ошибка: {e}"
            print(f"{it.id:16} {size:>12}  {it.name}")
        return 0

    if cmd == "clean" and len(argv) >= 2:
        ids = list(cleaner.BY_ID) if argv[1] == "all" else argv[1:]
        total = 0
        for i in ids:
            it = cleaner.BY_ID.get(i)
            if it is None:
                print(f"Нет пункта '{i}'. Список: python main.py scan")
                return 1
            if it.admin and not engine.is_admin():
                print(f"{i}: пропущено, нужен админ")
                continue
            try:
                freed = it.clean()
                total += freed
                print(f"{i}: освобождено {cleaner.fmt_size(freed)}")
                if it.warning:
                    print(f"  Внимание: {it.warning}")
            except Exception as e:
                print(f"{i}: ошибка: {e}")
        if total:
            cleaner.log(total)
        print(f"Всего освобождено {cleaner.fmt_size(total)}")
        return 0

    if cmd in ("on", "off") and len(argv) >= 2:
        t = tweaks.get(argv[1])
        if t is None:
            print(f"Нет твика '{argv[1]}'. Список: python main.py list")
            return 1
        if not engine.is_supported(t):
            print(f"Твик не поддерживается на Windows {engine.windows_version()}")
            return 1
        if not engine.is_admin():
            print("Запусти от имени администратора")
            return 1
        other = engine.other_user()
        if other:
            print(f"Внимание: запущено не от пользователя {other}, настройки HKCU применятся другому профилю")

        try:
            if cmd == "on":
                if "--no-restore" not in argv and not engine.auto_restore_point_msg()[0]:
                    print("Точка восстановления не создана (возможно, выключена защита системы)")
                warning = engine.apply_tweak(t)
            else:
                warning = engine.revert_tweak(t)
        except Exception as e:
            print(f"Ошибка: {e}")
            return 1

        if warning:
            print(f"Внимание: {warning}")
        print(f"{t['id']}: {engine.get_state(t)}")
        if t.get("after"):
            print(f"Дальше: {t['after']}")
        return 0

    print(__doc__)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
