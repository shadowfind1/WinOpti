using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinOpti.Sys;
using WinOpti.Tweaks;

namespace WinOpti.UI
{
    /// <summary>Включённый через WinOpti твик с кнопкой «Откатить».</summary>
    public sealed class AppliedViewModel : INotifyPropertyChanged
    {
        public Tweak T { get; }
        public event PropertyChangedEventHandler PropertyChanged;
        public event Action Reverted;
        readonly bool _admin;
        bool _busy, _done, _error;
        string _message;

        public AppliedViewModel(Tweak t, bool admin)
        {
            T = t;
            _admin = admin;
            RevertCommand = new RelayCommand(async () => await RevertAsync());
        }

        public string Name => T.Name;
        public string Category => TweaksPage.Cats.FirstOrDefault(c => c.id == T.Category).name ?? "";
        public bool Done => _done;
        public bool CanRevert => _admin && !_busy && !_done;
        public string ButtonText => _done ? "Откачено" : _busy ? "Откатываю…" : "Откатить";
        public string Message { get => _message; private set { _message = value; Changed(); Changed(nameof(HasMessage)); Changed(nameof(MessageBrush)); } }
        public bool HasMessage => !string.IsNullOrEmpty(_message);
        public Brush MessageBrush => (Brush)Application.Current.FindResource(_error ? "Red" : "Muted");
        public ICommand RevertCommand { get; }

        public async Task RevertAsync()
        {
            if (!CanRevert) return;
            _busy = true;
            _error = false;
            Refresh();
            try
            {
                var warn = await Task.Run(() => TweakEngine.Revert(T));
                _done = true;
                Message = warn;
            }
            catch (Exception e)
            {
                App.Log(e);
                _error = true;
                Message = "Не получилось: " + e.Message;
            }
            _busy = false;
            Refresh();
            if (_done) Reverted?.Invoke();
        }

        void Refresh() { Changed(nameof(CanRevert)); Changed(nameof(ButtonText)); }
        void Changed([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public sealed class PointRow
    {
        public string When { get; set; }
        public string Description { get; set; }
    }

    public sealed class HistoryRow
    {
        public string When { get; set; }
        public string Text { get; set; }
        public string Glyph { get; set; }
        public Brush Brush { get; set; }
        public Brush Soft { get; set; }
    }

    public partial class RestorePage : UserControl
    {
        readonly bool _admin = SysInfo.IsAdmin;
        List<AppliedViewModel> _applied = new List<AppliedViewModel>();
        List<HistoryRow> _history = new List<HistoryRow>();
        bool _loaded;
        const int HistoryShown = 15;

        public RestorePage()
        {
            InitializeComponent();
            AdminBanner.Visibility = _admin ? Visibility.Collapsed : Visibility.Visible;
            OpenRstrui.IsEnabled = _admin;
            // кнопки внутри списка при загрузке просят прокрутить к себе — страница должна открываться сверху
            ((FrameworkElement)Scroll.Content).RequestBringIntoView += (s, e) => e.Handled = true;
            IsVisibleChanged += async (s, e) =>
            {
                if (!IsVisible) return;
                // журнал перечитываем при каждом открытии: на других страницах могли что-то поменять
                LoadApplied();
                LoadHistory();
                if (!_loaded)
                {
                    _loaded = true;
                    Dispatcher.BeginInvoke(new Action(Scroll.ScrollToTop), System.Windows.Threading.DispatcherPriority.Loaded);
                    await LoadPointsAsync();
                }
            };
        }

        // ---------- изменения WinOpti ----------

        void LoadApplied()
        {
            var ids = TweakEngine.Applied();
            _applied = Tweak.LoadAll().Where(t => ids.Contains(t.Id)).Select(t => new AppliedViewModel(t, _admin)).ToList();
            foreach (var vm in _applied) vm.Reverted += () => { UpdateApplied(); LoadHistory(); };
            AppliedList.ItemsSource = _applied;
            UpdateApplied();
        }

        void UpdateApplied()
        {
            int n = _applied.Count(v => !v.Done);
            AppliedTotal.Text = n == 0 ? "Ничего не изменено" : $"{n} {Plural(n, "твик включён", "твика включено", "твиков включено")}";
            RevertAllButton.IsEnabled = _admin && n > 0;
            AppliedToggle.Visibility = _applied.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowAppliedList(_appliedOpen);
        }

        bool _appliedOpen;

        void ShowAppliedList(bool open)
        {
            _appliedOpen = open && _applied.Count > 0;
            AppliedList.Visibility = _appliedOpen ? Visibility.Visible : Visibility.Collapsed;
            AppliedToggle.Content = _appliedOpen ? "Скрыть список" : $"Показать список ({_applied.Count})";
        }

        void OnAppliedToggle(object sender, RoutedEventArgs e) => ShowAppliedList(!_appliedOpen);

        async void OnRevertAll(object sender, RoutedEventArgs e)
        {
            var left = _applied.Where(v => !v.Done).ToList();
            if (left.Count == 0) return;
            var answer = MessageBox.Show(Window.GetWindow(this),
                $"Откатить все {left.Count} {Plural(left.Count, "твик", "твика", "твиков")}? Вернутся настройки, которые были до WinOpti.",
                "WinOpti", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            RevertAllButton.IsEnabled = false;
            int i = 0;
            foreach (var vm in left)
            {
                RevertAllText.Text = $"Откатываю {++i} из {left.Count}…";
                await vm.RevertAsync();
            }
            RevertAllText.Text = "Откатить всё";
            UpdateApplied();
        }

        // ---------- точки восстановления ----------

        async Task LoadPointsAsync()
        {
            bool on = TweakEngine.ProtectionOn;
            CreatePoint.IsEnabled = _admin;
            CreatePointText.Text = on ? "Создать точку" : "Включить защиту";
            if (!_admin)
            {
                PointsList.ItemsSource = null;
                return;
            }
            List<PointRow> points;
            try
            {
                points = await Task.Run(() => SysInfo.Wmi("SELECT SequenceNumber, Description, CreationTime FROM SystemRestore", @"root\default")
                    .Select(p => (seq: Convert.ToInt64(p["SequenceNumber"]), desc: p["Description"] as string, time: ManagementDateTimeConverter.ToDateTime(p["CreationTime"] as string)))
                    .OrderByDescending(p => p.seq)
                    .Select(p => new PointRow { When = p.time.ToString("d MMMM, HH:mm", Ru), Description = p.desc })
                    .ToList());
            }
            catch (Exception ex)
            {
                App.Log(ex);
                points = new List<PointRow>();
            }
            PointsList.ItemsSource = points;
            if (!on) ShowPointsMessage("Защита системы выключена — Windows не создаёт точки восстановления. Нажми «Включить защиту»: WinOpti включит её для диска C: и сразу создаст точку.", "Orange");
            else if (points.Count == 0) ShowPointsMessage("Точек восстановления пока нет. Создай первую — это займёт около минуты.", "Muted");
        }

        void ShowPointsMessage(string text, string brush)
        {
            PointsMessage.Text = text;
            PointsMessage.Foreground = (Brush)FindResource(brush);
            PointsMessage.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        }

        async void OnCreatePoint(object sender, RoutedEventArgs e)
        {
            CreatePoint.IsEnabled = false;
            bool on = TweakEngine.ProtectionOn;
            CreatePointText.Text = on ? "Создаю…" : "Включаю…";
            ShowPointsMessage("Windows делает снимок системы, обычно это меньше минуты. Компьютер в это время может немного притормаживать.", "Muted");
            string err = null;
            if (!on)
            {
                var (code, output) = await Task.Run(() => SysInfo.Run("powershell", $"-NoProfile -Command \"$ErrorActionPreference='Stop'; Enable-ComputerRestore -Drive '{Environment.GetEnvironmentVariable("SystemDrive")}\\'\""));
                if (code != 0) err = "Не удалось включить защиту системы: " + output.Trim().Split('\n')[0];
            }
            if (err == null)
            {
                CreatePointText.Text = "Создаю…";
                var e2 = await Task.Run(() => TweakEngine.CreateRestorePoint("WinOpti — вручную"));
                if (e2 != null) err = "Точка не создана: " + e2;
            }
            if (err == null) TweakEngine.LogEntry("restore_point", "Создана точка восстановления", "restore_point");
            await LoadPointsAsync();
            ShowPointsMessage(err ?? "Готово: точка восстановления создана.", err == null ? "Green" : "Red");
            LoadHistory();
        }

        void OnOpenRstrui(object sender, RoutedEventArgs e)
        {
            // сам откат к точке делает мастер Windows: он покажет, какие программы затронет, и перезагрузит ПК
            try { Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true }); }
            catch (Exception ex) { ShowPointsMessage("Не удалось открыть восстановление системы: " + ex.Message, "Red"); }
        }

        // ---------- журнал ----------

        static readonly CultureInfo Ru = new CultureInfo("ru-RU");

        void LoadHistory()
        {
            _history = TweakEngine.History().Select(h => Row(h.time, h.name, h.action)).ToList();
            HistoryEmpty.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShowHistory(HistoryList.Tag is bool all && all);
        }

        void ShowHistory(bool all)
        {
            HistoryList.Tag = all;
            HistoryList.ItemsSource = all ? _history : _history.Take(HistoryShown).ToList();
            HistoryMore.Visibility = !all && _history.Count > HistoryShown ? Visibility.Visible : Visibility.Collapsed;
            HistoryMore.Content = $"Показать всё ({_history.Count})";
        }

        void OnHistoryMore(object sender, RoutedEventArgs e) => ShowHistory(true);

        HistoryRow Row(string time, string name, string action)
        {
            string text, glyph, color;
            switch (action)
            {
                case "on": text = "Включён твик «" + name + "»"; glyph = ""; color = "Green"; break;
                case "off": text = "Откачен твик «" + name + "»"; glyph = ""; color = "Blue"; break;
                case "clean": text = name; glyph = ""; color = "Accent"; break;
                case "startup_off": text = "Убрано из автозагрузки: " + Strip(name); glyph = ""; color = "Orange"; break;
                case "startup_on": text = "Возвращено в автозагрузку: " + Strip(name); glyph = ""; color = "Green"; break;
                case "restore_point": text = name; glyph = ""; color = "Blue"; break;
                default: text = name; glyph = ""; color = "Muted"; break;
            }
            return new HistoryRow { When = When(time), Text = text, Glyph = glyph, Brush = (Brush)FindResource(color), Soft = Soft(color) };
        }

        static string Strip(string name) => name.StartsWith("Автозагрузка: ") ? name.Substring("Автозагрузка: ".Length) : name;

        Brush Soft(string color)
        {
            var key = color + "Soft";
            return TryFindResource(key) as Brush ?? (Brush)FindResource("CardAlt");
        }

        /// <summary>«сегодня, 14:02», «вчера, 23:10», «26 октября, 00:25».</summary>
        static string When(string time)
        {
            if (!DateTime.TryParseExact(time, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return time;
            if (t.Date == DateTime.Today) return "сегодня, " + t.ToString("HH:mm");
            if (t.Date == DateTime.Today.AddDays(-1)) return "вчера, " + t.ToString("HH:mm");
            return t.ToString(t.Year == DateTime.Today.Year ? "d MMMM, HH:mm" : "d MMMM yyyy", Ru);
        }

        static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            return m10 == 1 && m100 != 11 ? one : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? few : many;
        }
    }
}
