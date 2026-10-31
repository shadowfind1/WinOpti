# WinOpti 2

Программа для игроков на Windows 10 и 11. Находит настоящие причины низкого FPS и фризов, исправляет их в один клик
и показывает результат цифрами: замер FPS до и после, прямо в твоей игре.

**Русский** · [English](#english)

![Диагностика](docs/diag.png)

| Замер FPS и «Что мешало игре» | Проверки |
|---|---|
| ![Замер FPS](docs/fps.png) | ![Проверки](docs/checks.png) |

## Скачать

> **WinOpti 2 сейчас в бета-версии.** Стабильная первая версия — [WinOpti 1.0.4](../../releases/tag/v1.0.4).

1. Открой [WinOpti 2.0.0 beta](../../releases/tag/v2.0.0-beta) и скачай `WinOpti-2.0.0-beta.exe` — это один файл.
2. Запусти и разреши запуск от имени администратора: без этого Windows не даст менять настройки и замерять FPS.

Нужен .NET Framework 4.8 — он уже встроен в Windows 10 (с 2019 года) и Windows 11.

> **SmartScreen может предупредить** о неизвестной программе: exe пока не подписан цифровой подписью.
> Нажми «Подробнее» → «Выполнить в любом случае». Весь исходный код — в этом репозитории.

## Что умеет

| Раздел | Что внутри |
|---|---|
| **Диагностика** | 14 проверок того, что реально съедает FPS: частота монитора ниже возможной, медленная или разная память, план питания, VBS, игровой режим, Game DVR, ускорение мыши, драйвер видеокарты, игры на HDD, фоновая нагрузка. Оценка ПК от 0 до 100 и исправление в один клик |
| **Замер FPS** | Средний FPS, 1% и 0.1% low, фризы — через Intel PresentMon, как у обзорщиков. Серия из 3 проходов с разбросом. «До» и «После», картинка результата для друзей |
| **Что мешало игре** | Во время замера WinOpti смотрит, какие программы грузили процессор и видеокарту и кто просыпался в момент фризов. Видеокарта NVIDIA: перегрев, сброс частоты, нехватка питания. Процессор: сброс частоты под нагрузкой. Для каждой находки — кнопка исправления, а потом «Исправить всё и замерить снова» |
| **Проверки** | Авто-игровой режим (сам включается, когда запускаешь игру), реальная частота опроса мыши, пинг до серверов Valve и где проблема — в Wi-Fi или у провайдера, игры на правильной видеокарте, монитор в разъёме видеокарты, настройки драйвера NVIDIA, советы для AMD Radeon |
| **Твики** | 32 твика с описанием простыми словами, риском и откатом: телеметрия, фон, интерфейс, игры и ввод |
| **Очистка** | Временные файлы, кэш обновлений, отчёты об ошибках, старые установщики драйверов, кэш шейдеров |
| **Автозагрузка** | Программы, службы и приложения из Microsoft Store: сколько каждая занимает памяти прямо сейчас, подсказки, что можно выключить |
| **Восстановление** | Откат любого изменения WinOpti, точки восстановления Windows, понятный журнал действий |

Без лагов: окно открывается за полсекунды, фоновый игровой режим занимает около 40 МБ и меньше 0,2% процессора.

## Что уходит в интернет

По умолчанию — ничего. Сеть нужна только для проверки пинга: список серверов берётся у Steam, сами пинги идут
на серверы Valve и твой роутер. Сравнение «Твой ПК против похожих» включается только по кнопке и отправляет
лишь модели процессора и видеокарты, объём памяти, разрешение экрана и FPS.

## Собрать из исходников

```
dotnet build WinOpti.csproj -c Release
```

C# и WPF на .NET Framework 4.8. Сервер для сравнения ПК — в папке `cloud/` (Cloudflare Worker).

Первая версия на Python — в ветке [`v1`](../../tree/v1).

## Благодарности

- [Intel PresentMon](https://github.com/GameTechDev/PresentMon) — замер кадров (MIT, лицензия в `Tools/`).
- Шрифты [Inter Tight](https://github.com/rsms/inter-tight) и [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) — SIL Open Font License.

---

<a id="english"></a>

# WinOpti 2 (English)

A tool for PC gamers on Windows 10 and 11. It finds the real causes of low FPS and stutter, fixes them in one click
and proves the result with numbers: a before/after FPS benchmark in your own game.

> **The interface is in Russian for now; an English version is coming.** WinOpti 2 is in beta — the stable
> release is [WinOpti 1.0.4](../../releases/tag/v1.0.4).

**Download:** [WinOpti 2.0.0 beta](../../releases/tag/v2.0.0-beta) → `WinOpti-2.0.0-beta.exe`, run as administrator.
Requires .NET Framework 4.8 (built into Windows 10 since 2019 and Windows 11). The exe is not code-signed yet,
so SmartScreen may warn: click "More info" → "Run anyway".

**Features**

- **Diagnostics** — 14 checks for what actually costs FPS (monitor below its max refresh rate, slow or mixed RAM,
  power plan, VBS, Game DVR, mouse acceleration, GPU driver age, games on an HDD, background load), a 0–100 score
  and one-click fixes.
- **FPS benchmark** — average FPS, 1% and 0.1% lows and stutters via Intel PresentMon, a 3-pass series with spread,
  before/after comparison and a shareable result card.
- **What hurt your game** — while benchmarking, WinOpti records which processes used CPU/GPU and which woke up during
  stutters, plus NVIDIA GPU temperature, clock drops and power limits, and CPU frequency drops. Every finding has a
  fix button, then "fix all and benchmark again".
- **Checks** — automatic game mode, real mouse polling rate, ping and jitter to Valve servers (Wi-Fi vs ISP),
  games on the right GPU, monitor plugged into the GPU, NVIDIA driver settings, AMD Radeon tips.
- **Tweaks, cleanup, startup manager, restore** — every change can be rolled back.

**Privacy:** nothing is sent by default. The ping test talks to Steam's public server list and Valve servers.
The "your PC vs similar PCs" comparison is opt-in and sends only CPU/GPU model, RAM size, resolution and FPS.

**Build:** `dotnet build WinOpti.csproj -c Release` (C#/WPF, .NET Framework 4.8). The Python v1 lives in the
[`v1`](../../tree/v1) branch.

## License

[MIT](LICENSE)
