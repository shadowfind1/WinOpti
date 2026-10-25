"""winopt/lang_en.py: английский перевод интерфейса. Ключ — русская строка из кода (как в tr("…")),
значение — перевод. Плейсхолдеры {name} и т.п. должны совпадать. Названия и описания твиков
и пресетов переводятся в tweaks.json / presets.json (поля *_en)."""

EN = {
    # ---------- вкладки и меню ----------
    "Пресеты": "Presets",
    "Готовые наборы твиков: включаются одной кнопкой": "Ready-made tweak sets, applied with one click",
    "Приложения": "Apps",
    "Удаление программ вместе с остатками и встроенных приложений Windows":
        "Uninstall programs with their leftovers, and remove built-in Windows apps",
    "Автозагрузка": "Startup",
    "Программы, которые запускаются вместе с Windows": "Programs that start with Windows",
    "Очистка": "Cleanup",
    "Временные файлы, кэш обновлений и корзина": "Temporary files, update cache and Recycle Bin",
    "Восстановление": "Restore",
    "Откат изменений и точки восстановления Windows": "Undo changes and Windows restore points",
    "Телеметрия и фон": "Telemetry & background",
    "Слежка и фоновые службы, которые тратят ресурсы ПК": "Tracking and background services that use PC resources",
    "Интерфейс": "Interface",
    "Анимации, прозрачность и отклик интерфейса": "Animations, transparency and UI responsiveness",
    "Производительность": "Performance",
    "Питание, запуск и фоновые процессы": "Power, startup and background processes",
    "Игры и ввод": "Gaming & input",
    "Меньше помех в играх, точная мышь, быстрая клавиатура":
        "Fewer interruptions in games, precise mouse, fast keyboard",
    "Система": "System",
    "Обновления и системные настройки": "Updates and system settings",
    "БЫСТРЫЙ СТАРТ": "QUICK START",
    "ОПТИМИЗАЦИЯ": "OPTIMIZATION",
    "ОБСЛУЖИВАНИЕ": "MAINTENANCE",
    "БЕЗОПАСНОСТЬ": "SAFETY",
    "Точка восстановления": "Restore point",
    "●  администратор": "●  administrator",
    "●  нет прав администратора": "●  not administrator",
    "Подожди, пока закончится текущая операция, и переключи язык ещё раз.":
        "Wait for the current operation to finish, then switch the language again.",

    # ---------- общее ----------
    "Нет прав администратора: изменения недоступны. Закрой окно и запусти от имени администратора.":
        "No administrator rights: changes are unavailable. Close the window and run it as administrator.",
    "WinOpti запущен не от пользователя {user}, а от другой учётной записи. Настройки интерфейса, проводника и "
    "мыши применятся ей, а не {user}. Лучше войти под {user} с правами администратора.":
        "WinOpti is running under a different account, not {user}. Interface, Explorer and mouse settings will "
        "apply to that account instead of {user}. Better sign in as {user} with administrator rights.",
    "Ошибка: {err}": "Error: {err}",
    "Ошибка интерфейса ({kind}): {err}": "Interface error ({kind}): {err}",
    "Обновить": "Refresh",
    "Читаю состояние…": "Reading state…",
    "читаю состояние…": "reading state…",
    "{on} из {total} включено": "{on} of {total} enabled",
    "Внимание: {msg}.": "Warning: {msg}.",
    "Ошибки: {list}": "Errors: {list}",
    "код {rc}": "code {rc}",
    "Б": "B",
    "КБ": "KB",
    "МБ": "MB",
    "ГБ": "GB",
    "Готово": "Done",
    "Закрыть": "Close",
    "Отмена": "Cancel",
    "Удалить": "Remove",
    "Удаляю…": "Removing…",
    "Загружаю список…": "Loading the list…",
    "Не удалось получить список: {err}": "Couldn't get the list: {err}",
    "лучше не трогать": "better keep",
    "нужны права администратора": "administrator rights required",
    "Нужны права администратора": "Administrator rights required",

    # ---------- твики ----------
    "низкий риск": "low risk",
    "средний риск": "medium risk",
    "высокий риск": "high risk",
    "включено": "enabled",
    "выключено": "disabled",
    "частично": "partial",
    "не удалось прочитать": "couldn't read",
    "недоступно на Windows {v}": "not available on Windows {v}",
    "применяю…": "applying…",
    "откатываю…": "reverting…",
    "{name}: готово.": "{name}: done.",
    "{name}: ошибка: {err}": "{name}: error: {err}",
    "Точка восстановления не создана: {msg}": "Restore point not created: {msg}",

    # ---------- пресеты ----------
    "Файл presets.json не найден или пуст.": "presets.json is missing or empty.",
    "Применить пресет": "Apply preset",
    "Откатить": "Revert",
    "недоступно": "not available",
    "всё включено: {on} из {total}": "all enabled: {on} of {total}",
    "включено {on} из {total}": "{on} of {total} enabled",
    "Пресет «{name}»: применяю…": "Preset “{name}”: applying…",
    "Пресет «{name}»: откатываю…": "Preset “{name}”: reverting…",
    "Пресет «{name}»: всё уже включено.": "Preset “{name}”: everything is already enabled.",
    "Пресет «{name}» включит:\n\n{items}\n\nВсё можно откатить на вкладке «Восстановление».":
        "Preset “{name}” will enable:\n\n{items}\n\nEverything can be undone on the Restore tab.",
    "Пресет «{name}»: откатывать нечего.": "Preset “{name}”: nothing to revert.",
    "Откатить твики пресета «{name}», включённые через WinOpti?\n\n{items}\n\nОни могут входить и в другие пресеты.":
        "Revert the tweaks of preset “{name}” that were enabled by WinOpti?\n\n{items}\n\n"
        "They may also belong to other presets.",
    "точка восстановления не создана: {msg}": "restore point not created: {msg}",
    "Пресет «{name}»: включено твиков: {done} из {total}.": "Preset “{name}”: tweaks enabled: {done} of {total}.",
    "Пресет «{name}»: откачено твиков: {done} из {total}.": "Preset “{name}”: tweaks reverted: {done} of {total}.",
    "Дальше: {what}.": "Next: {what}.",

    # ---------- приложения ----------
    "Программы": "Programs",
    "Встроенные Windows": "Built-in Windows",
    "Поиск по названию или издателю": "Search by name or publisher",
    "{shown} из {total}": "{shown} of {total}",
    "Приложения удаляются для всех пользователей и не возвращаются после обновлений Windows. Вернуть любое можно "
    "из Microsoft Store.":
        "Apps are removed for all users and don't come back after Windows updates. Any of them can be "
        "reinstalled from the Microsoft Store.",
    "Сначала запускается деинсталлятор программы, потом WinOpti находит оставшиеся папки, ярлыки и записи реестра. "
    "Файлы удаляются в корзину, реестр — с резервной копией.":
        "The program's own uninstaller runs first, then WinOpti finds leftover folders, shortcuts and registry "
        "entries. Files go to the Recycle Bin, registry keys are backed up first.",
    "Показаны первые {n}. Уточни поиск, чтобы увидеть остальные.":
        "Showing the first {n}. Refine the search to see the rest.",
    "можно удалить": "safe to remove",
    "Удалить «{name}»?\n\nПриложение удалится для всех пользователей и не вернётся после обновлений. Вернуть его "
    "можно из Microsoft Store.":
        "Remove “{name}”?\n\nThe app will be removed for all users and won't come back after updates. "
        "You can reinstall it from the Microsoft Store.",
    "это приложение лучше оставить": "this app is better kept",
    "Удаляю «{name}»…": "Removing “{name}”…",
    "«{name}» не удалено: {err}": "“{name}” was not removed: {err}",
    "«{name}» удалено.": "“{name}” removed.",
    "удаление программы": "uninstall program",
    "Сначала запустится деинсталлятор самой программы: пройди его до конца. Потом WinOpti найдёт оставшиеся папки, "
    "ярлыки и записи реестра и покажет их списком. Удалится только то, что ты отметишь: файлы — в корзину, реестр "
    "— с резервной копией.":
        "First the program's own uninstaller will start: go through it to the end. Then WinOpti will find "
        "leftover folders, shortcuts and registry entries and list them. Only what you tick gets removed: files "
        "go to the Recycle Bin, registry keys are backed up first.",
    "Папка: {path}": "Folder: {path}",
    "«Только найти остатки» — если деинсталлятор не работает или программа уже удалена.":
        "“Only find leftovers” — if the uninstaller doesn't work or the program is already gone.",
    "Запустить деинсталлятор": "Run uninstaller",
    "Только найти остатки": "Only find leftovers",
    "Деинсталлятор не запустился: {err}. Можно сразу поискать остатки.":
        "The uninstaller didn't start: {err}. You can search for leftovers right away.",
    "Найти остатки": "Find leftovers",
    "Деинсталлятор запущен (его окно могло открыться позади WinOpti). Пройди его до конца: WinOpti сам заметит, что "
    "программа удалилась, и начнёт искать остатки.":
        "The uninstaller is running (its window may have opened behind WinOpti). Go through it to the end: "
        "WinOpti will notice when the program is gone and start looking for leftovers.",
    "Деинсталлятор закрыт — искать остатки": "Uninstaller closed — find leftovers",
    "Ищу остатки…": "Looking for leftovers…",
    "Не удалось найти остатки: {err}": "Couldn't search for leftovers: {err}",
    "Программа всё ещё установлена, а остатков по имени не найдено.":
        "The program is still installed, and no leftovers were found by name.",
    "Готово: программа удалена, остатков не найдено.": "Done: the program is removed, no leftovers found.",
    "Похоже, программа НЕ удалена: деинсталлятор отменён или завершился с ошибкой. Ниже — сама программа целиком. "
    "Галочки сняты: отмечай, только если хочешь удалить её принудительно.":
        "Looks like the program was NOT removed: the uninstaller was cancelled or failed. Below is the whole "
        "program itself. Nothing is ticked: tick items only if you want to force-remove it.",
    "Программа удалена. Найдено остатков: {n}, примерно {size}. Пункты без галочки WinOpti не уверен, что они "
    "относятся только к этой программе: проверь их сам.":
        "The program is removed. Leftovers found: {n}, about {size}. Unticked items may not belong only to this "
        "program: check them yourself.",
    "папка": "folder",
    "файл": "file",
    "реестр": "registry",
    "Закрыть, ничего не удалять": "Close without removing",
    "Удалить выбранное ({n}, {size})": "Remove selected ({n}, {size})",
    "Удалено пунктов: {n}, освобождено {size}. Файлы — в корзине.":
        "Items removed: {n}, freed {size}. Files are in the Recycle Bin.",
    "Резервная копия реестра: {path}\nЧтобы вернуть реестр, открой этот файл двойным кликом.":
        "Registry backup: {path}\nTo restore the registry, double-click this file.",
    # остатки (apps.py)
    "(имя общее с другой установленной программой: проверь)": "(name shared with another installed program: check)",
    "(имя не совпадает с программой: проверь, что внутри)": "(name doesn't match the program: check what's inside)",
    "у программы не указан деинсталлятор": "the program has no uninstaller registered",
    "папка установки": "install folder",
    "папка программы": "program folder",
    "ярлык в меню «Пуск»": "Start menu shortcut",
    "ярлык на рабочем столе": "desktop shortcut",
    "по имени в {folder}": "by name in {folder}",
    "раздел реестра программы": "program's registry key",
    "раздел реестра {key}": "registry key {key}",
    "запись в списке установленных программ": "entry in the installed programs list",
    "не удалось удалить (код {rc}), возможно, файл занят": "couldn't delete (code {rc}), the file may be in use",
    "резервная копия {key} не создана: {err}": "backup of {key} not created: {err}",
    "Удалена программа: {name}": "Program removed: {name}",
    "Удалено приложение: {name}": "App removed: {name}",
    "странное имя пакета": "unexpected package name",
    "кодек: без него не откроются некоторые фото и видео": "codec: some photos and videos won't open without it",
    "панель управления драйвера": "driver control panel",
    "библиотека: нужна другим программам": "library: other programs need it",
    "драйвер": "driver",
    "античит: без него не запустятся игры": "anti-cheat: games won't start without it",
    "среда разработки: могут зависеть другие программы": "development tool: other programs may depend on it",
    # встроенные приложения
    "Погода (MSN)": "Weather (MSN)",
    "Новости (MSN)": "News (MSN)",
    "Поиск Bing": "Bing Search",
    "Техническая поддержка": "Get Help",
    "Советы": "Tips",
    "Microsoft 365 (реклама Office)": "Microsoft 365 (Office ads)",
    "Люди": "People",
    "Центр отзывов": "Feedback Hub",
    "Карты": "Maps",
    "Кино и ТВ": "Movies & TV",
    "Медиаплеер / Groove Музыка": "Media Player / Groove Music",
    "Связь с телефоном": "Phone Link",
    "Портал смешанной реальности": "Mixed Reality Portal",
    "Средство 3D-просмотра": "3D Viewer",
    "OneNote для Windows 10": "OneNote for Windows 10",
    "Кошелёк": "Wallet",
    "Microsoft PC Manager (ещё один «оптимизатор»)": "Microsoft PC Manager (yet another “optimizer”)",
    "Виджеты": "Widgets",
    "Microsoft Teams (личный)": "Microsoft Teams (personal)",
    "Outlook (новый)": "Outlook (new)",
    "Почта и Календарь": "Mail and Calendar",
    "Xbox (старое приложение)": "Xbox (old app)",
    "Xbox: нужен для Game Pass": "Xbox: needed for Game Pass",
    "Xbox Game Bar: запись игр, Win+G": "Xbox Game Bar: game recording, Win+G",
    "Xbox Game Bar (компонент)": "Xbox Game Bar (component)",
    "Xbox: преобразование речи в текст": "Xbox: speech to text",
    "Вход в Xbox: нужен играм с аккаунтом Microsoft": "Xbox sign-in: needed by games with a Microsoft account",
    "Xbox TCUI: нужен играм с аккаунтом Microsoft": "Xbox TCUI: needed by games with a Microsoft account",
    "Часы и будильник": "Clock and Alarms",
    "Звукозапись": "Sound Recorder",
    "Камера": "Camera",
    "Калькулятор": "Calculator",
    "Фотографии: стандартный просмотр картинок": "Photos: the default image viewer",
    "Ножницы (Win+Shift+S)": "Snipping Tool (Win+Shift+S)",
    "Блокнот": "Notepad",
    "Терминал": "Terminal",
    "Записки": "Sticky Notes",
    "Copilot (компонент)": "Copilot (component)",
    "Быстрая помощь": "Quick Assist",
    "Игра от King (Candy Crush и т.п.)": "King game (Candy Crush etc.)",

    # ---------- автозагрузка ----------
    "Что запускается вместе с Windows": "What starts with Windows",
    "Чем меньше программ стартует при входе, тем быстрее загрузка и больше свободной памяти. Выключенная программа "
    "не удаляется: её можно включить обратно в любой момент, и она так же выключена в Диспетчере задач.":
        "The fewer programs start at sign-in, the faster Windows loads and the more memory stays free. A disabled "
        "program isn't removed: you can turn it back on at any time, and it shows as disabled in Task Manager too.",
    "программ запускается с Windows": "programs start with Windows",
    "Обновить список": "Refresh list",
    "{on} из {total}": "{on} of {total}",
    "Не удалось прочитать автозагрузку: {err}": "Couldn't read startup entries: {err}",
    "Автозагрузка пуста.": "Nothing starts with Windows.",
    "можно выключить": "safe to disable",
    "Файл программы не найден: запись осталась от удалённой программы, её можно выключить.":
        "Program file not found: the entry is left over from a removed program and can be disabled.",
    "включаю…": "enabling…",
    "выключаю…": "disabling…",
    "{name}: будет запускаться вместе с Windows.": "{name}: will start with Windows.",
    "{name}: больше не запускается вместе с Windows.": "{name}: no longer starts with Windows.",
    "задача планировщика": "scheduled task",
    "задачи планировщика не прочитаны: {err}": "scheduled tasks couldn't be read: {err}",
    "Автозагрузка: {name}": "Startup: {name}",
    "реестр, текущий пользователь": "registry, current user",
    "реестр, все пользователи": "registry, all users",
    "реестр, все пользователи (32-бит)": "registry, all users (32-bit)",
    "папка «Автозагрузка», текущий пользователь": "Startup folder, current user",
    "папка «Автозагрузка», все пользователи": "Startup folder, all users",
    "значок «Безопасности Windows»": "Windows Security tray icon",
    "античит Riot: без него не запустятся Valorant и League of Legends":
        "Riot anti-cheat: Valorant and League of Legends won't start without it",
    "античит FACEIT": "FACEIT anti-cheat",
    "звуковой драйвер": "audio driver",
    "драйвер тачпада": "touchpad driver",
    "драйвер Intel": "Intel driver",
    "антивирус": "antivirus",
    "языковая панель и ввод текста": "language bar and text input",
    "ПО мыши или клавиатуры: настройки DPI, макросы и подсветка": "mouse or keyboard software: DPI, macros and lighting",
    "лаунчер, сам запустится при открытии игры": "launcher, starts by itself when you open a game",
    "лаунчер Epic Games": "Epic Games launcher",
    "лаунчер Riot": "Riot launcher",
    "лаунчер EA": "EA launcher",
    "лаунчер Battle.net": "Battle.net launcher",
    "лаунчер Ubisoft": "Ubisoft launcher",
    "лаунчер Roblox": "Roblox launcher",
    "клиент FACEIT: запусти его перед игрой": "FACEIT client: start it before playing",
    "мессенджер: оставь, если нужен сразу после входа": "messenger: keep it if you need it right after sign-in",
    "музыка, запускается за секунду вручную": "music, opens in a second when you need it",
    "облако: выключи, если не синхронизируешь файлы": "cloud: disable it if you don't sync files",
    "фоновый запуск браузера ради «быстрого старта»": "browser preloading in the background for a “fast start”",
    "обновления Google: проверятся сами при запуске Chrome": "Google updates: checked anyway when Chrome starts",
    "служебные программы и обновления Adobe": "Adobe helpers and updates",
    "проверка обновлений Java": "Java update check",
    "помощник iTunes": "iTunes helper",
    "фоновый мониторинг CCleaner": "CCleaner background monitoring",
    "торрент-клиент": "torrent client",

    # ---------- очистка ----------
    "Освободить место на диске": "Free up disk space",
    "WinOpti считает, сколько места занимает каждый пункт. Удаляется только то, что Windows и программы создают "
    "заново сами. Занятые файлы пропускаются.":
        "WinOpti measures how much space each item takes. Only things that Windows and programs recreate on their "
        "own are removed. Files in use are skipped.",
    "можно освободить (выбранные пункты)": "can be freed (selected items)",
    "Пересканировать": "Rescan",
    "Очистить выбранное": "Clean selected",
    "считаю…": "measuring…",
    "Нечего очищать: выбери пункты, где что-то найдено.": "Nothing to clean: select items where something was found.",
    "Удалить?\n\n{items}\n\nВсего примерно {size}. Вернуть эти файлы будет нельзя.":
        "Delete?\n\n{items}\n\nAbout {size} in total. These files can't be recovered.",
    "Очищаю…": "Cleaning…",
    "Очистка завершена, освобождено {size}.": "Cleanup finished, {size} freed.",
    "не удалось: {err}": "failed: {err}",
    "очищаю…": "cleaning…",
    "освобождено {size}": "{size} freed",
    "Временные файлы пользователя": "User temporary files",
    "Папка %TEMP%: остатки установщиков, распаковки архивов, кэш программ. Файлы младше часа не трогаем, чтобы не "
    "помешать работающим программам.":
        "The %TEMP% folder: installer leftovers, unpacked archives, program caches. Files younger than an hour are "
        "kept so running programs aren't disturbed.",
    "Временные файлы Windows": "Windows temporary files",
    "Папка Windows\\Temp: служебные временные файлы системы и установщиков. Файлы младше часа не трогаем.":
        "The Windows\\Temp folder: system and installer temporary files. Files younger than an hour are kept.",
    "Кэш обновлений Windows": "Windows Update cache",
    "Уже установленные обновления, скачанные в SoftwareDistribution\\Download. Не запускай очистку, пока Windows "
    "устанавливает обновления.":
        "Already installed updates downloaded to SoftwareDistribution\\Download. Don't run the cleanup while "
        "Windows is installing updates.",
    "Кэш оптимизации доставки": "Delivery Optimization cache",
    "Копии обновлений и приложений, которые Windows хранит, чтобы раздавать другим ПК.":
        "Copies of updates and apps that Windows keeps to share with other PCs.",
    "Отчёты об ошибках и дампы памяти": "Error reports and memory dumps",
    "Отчёты о сбоях программ и дампы памяти после «синих экранов». Нужны только для диагностики, могут занимать "
    "гигабайты.":
        "Crash reports and memory dumps from blue screens. Only needed for diagnostics, can take gigabytes.",
    "Корзина": "Recycle Bin",
    "Файлы, удалённые в корзину, на всех дисках. После очистки их уже не вернуть.":
        "Files in the Recycle Bin on all drives. After cleaning they can't be recovered.",
    "подозрительная папка {path}, пропускаю": "suspicious folder {path}, skipping",
    "службы {list} не запустились обратно; Windows запустит их сама при проверке обновлений или после перезагрузки":
        "services {list} didn't start again; Windows will start them itself when checking for updates or after "
        "a reboot",

    # ---------- восстановление ----------
    "откачено": "reverted",
    "очистка": "cleanup",
    "автозапуск выкл.": "startup off",
    "автозапуск вкл.": "startup on",
    "удалено": "removed",
    "Изменения WinOpti": "WinOpti changes",
    "Твики, которые ты включил через WinOpti. Откат возвращает значения, которые были до включения.":
        "Tweaks you enabled with WinOpti. Reverting restores the values that were there before.",
    "Откатить всё": "Revert all",
    "Точки восстановления Windows": "Windows restore points",
    "Точка — это снимок системы. Если после изменений что-то пошло не так, Windows можно вернуть к выбранной точке: "
    "нажми «Открыть восстановление системы» и выбери её в списке.":
        "A restore point is a snapshot of the system. If something goes wrong after changes, Windows can be rolled "
        "back to it: click “Open System Restore” and pick it from the list.",
    "Создать точку сейчас": "Create a point now",
    "Открыть восстановление системы": "Open System Restore",
    "Журнал": "Log",
    "Пока ничего не применено через WinOpti.": "Nothing has been applied with WinOpti yet.",
    "применено": "applied",
    "применено {time}": "applied {time}",
    "Журнал пуст.": "The log is empty.",
    "Не удалось получить список точек: {err}": "Couldn't get restore points: {err}",
    "Точек восстановления нет. Возможно, защита системы выключена для диска C:.":
        "No restore points. System Protection may be turned off for drive C:.",
    "Откатываю…": "Reverting…",
    "Откатывать нечего.": "Nothing to revert.",
    "Откатить все изменения, сделанные через WinOpti?": "Revert all changes made with WinOpti?",
    "Откатываю все изменения…": "Reverting all changes…",
    "Создаю точку восстановления…": "Creating a restore point…",
    "Не удалось открыть восстановление системы: {err}": "Couldn't open System Restore: {err}",
    "Откачено изменений: {n}.": "Changes reverted: {n}.",
    "Точка восстановления создана.": "Restore point created.",
    "Точка не создана: {msg}": "Restore point not created: {msg}",

    "Включить защиту системы": "Turn on System Protection",
    "Включаю защиту системы…": "Turning on System Protection…",
    "●  Защита системы включена": "●  System Protection is on",
    "●  Защита системы выключена: точки восстановления не создаются":
        "●  System Protection is off: restore points can't be created",
    "Защита системы включена.": "System Protection turned on.",
    "Не удалось включить защиту системы: {err}": "Couldn't turn on System Protection: {err}",

    # ---------- поиск ----------
    "Поиск твиков…": "Search tweaks…",
    "Ничего не найдено. Попробуй другое слово — ищу по названию и описанию.":
        "Nothing found. Try another word — names and descriptions are searched.",
    "Поиск: «{q}»": "Search: “{q}”",
    "Найдено твиков: {n}": "Tweaks found: {n}",
    "Показаны первые {n}. Уточни запрос.": "Showing the first {n}. Refine the query.",

    # ---------- свои настройки ----------
    "Свои настройки": "Your settings",
    "Сохрани включённые твики в файл и примени их на другом ПК или после переустановки Windows. Импорт только "
    "включает твики из файла и ничего не выключает.":
        "Save your enabled tweaks to a file and apply them on another PC or after reinstalling Windows. Import only "
        "enables the tweaks from the file and never disables anything.",
    "Экспорт в файл": "Export to file",
    "Импорт из файла": "Import from file",
    "Экспорт настроек": "Export settings",
    "Импорт настроек": "Import settings",
    "Читаю состояние твиков…": "Reading tweak states…",
    "Удалить все точки": "Delete all points",
    "Точек восстановления нет — удалять нечего.": "There are no restore points to delete.",
    "Удалить все точки восстановления Windows ({n} шт.)?\n\nВернуть систему к ним будет нельзя. Изменения WinOpti это не затронет: их по-прежнему можно откатить кнопками выше.":
        "Delete all Windows restore points ({n})?\n\nYou won't be able to roll the system back to them. WinOpti "
        "changes are not affected: you can still revert them with the buttons above.",
    "Удаляю точки восстановления…": "Deleting restore points…",
    "Удалено точек восстановления: {n}.": "Restore points deleted: {n}.",
    "Сохранено твиков: {n}. Файл: {path}": "Tweaks saved: {n}. File: {path}",
    "Не удалось сохранить файл: {err}": "Couldn't save the file: {err}",
    "Не удалось прочитать файл: {err}": "Couldn't read the file: {err}",
    "это не файл настроек WinOpti": "this is not a WinOpti settings file",
    "недоступно на этой Windows: {n}": "not available on this Windows: {n}",
    "неизвестно этой версии WinOpti: {n}": "unknown to this WinOpti version: {n}",
    "Всё из файла уже включено.": "Everything from the file is already enabled.",
    "Включить из файла:\n\n{items}": "Enable from the file:\n\n{items}",
    "Пропущено: {list}.": "Skipped: {list}.",
    "Всё можно откатить на вкладке «Восстановление».": "Everything can be undone on the Restore tab.",
    "импорт из файла": "import from file",

    # ---------- engine ----------
    "защита системы выключена: включи её на вкладке «Восстановление»":
        "System Protection is off: turn it on on the Restore tab",
    "Windows не создала новую точку": "Windows didn't create a new restore point",
    "служба {name} не найдена": "service {name} not found",
    "не удалось создать схему питания: {err}": "couldn't create the power plan: {err}",
    "этой схемы питания нет на ПК (часто так на ноутбуках с Modern Standby)":
        "this power plan doesn't exist on this PC (common on laptops with Modern Standby)",
    "проводник не запустился, запусти explorer.exe вручную: {err}":
        "Explorer didn't start, run explorer.exe manually: {err}",
    "не удалось применить сразу, нужен перезаход ({list})": "couldn't apply right away, sign out and back in ({list})",
    "журнал не записан: {err}": "log not written: {err}",
    "не удалось получить список": "couldn't get the list",
}
