using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinOpti.Cleanup;
using WinOpti.Sys;
using WinOpti.Tweaks;

namespace WinOpti.UI
{
    public sealed class CleanViewModel : INotifyPropertyChanged
    {
        public CleanItem Item { get; }
        public event PropertyChangedEventHandler PropertyChanged;
        public event Action SelectionChanged;
        readonly bool _admin;

        public CleanViewModel(CleanItem item, bool admin)
        {
            Item = item;
            _admin = admin;
            _checked = item.DefaultOn && CanSelect;
        }

        long? _size;
        bool _checked, _error;
        string _result;

        public string Name => Item.Name;
        public string Description => Item.Description;
        public bool CanSelect => _admin || !Item.Admin;
        public bool NeedsAdminVisible => !CanSelect;
        public bool Checked { get => _checked; set { _checked = value; Changed(); SelectionChanged?.Invoke(); } }
        public long Size => _size ?? 0;
        public long? SizeValue { get => _size; set { _size = value; Changed(nameof(SizeText)); Changed(nameof(SizeBrush)); } }
        public string SizeText => _size == null ? "…" : _size == -1 ? "—" : Cleaner.Format(_size.Value);
        public Brush SizeBrush => (Brush)Application.Current.FindResource(_size > 0 ? "Text" : "Dim");
        public string Result { get => _result; set { _result = value; Changed(); Changed(nameof(HasResult)); Changed(nameof(ResultBrush)); } }
        public bool Error { set { _error = value; Changed(nameof(ResultBrush)); } }
        public bool HasResult => !string.IsNullOrEmpty(_result);
        public Brush ResultBrush => (Brush)Application.Current.FindResource(_error ? "Red" : "Green");

        void Changed([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public partial class CleanupPage : UserControl
    {
        readonly List<CleanViewModel> _items;
        bool _loaded, _busy;
        /// <summary>В шапке — итог последней очистки. Держится, пока человек не поменяет выбор или не нажмёт «Проверить снова».</summary>
        bool _showResult;

        public CleanupPage()
        {
            InitializeComponent();
            bool admin = SysInfo.IsAdmin;
            AdminBanner.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
            _items = Cleaner.All.Select(i => new CleanViewModel(i, admin)).ToList();
            foreach (var vm in _items) vm.SelectionChanged += () => { HideResult(); UpdateTotals(); };
            List.ItemsSource = _items;
            IsVisibleChanged += async (s, e) => { if (IsVisible && !_loaded) { _loaded = true; await ScanAsync(); } };
        }

        async Task ScanAsync()
        {
            _busy = true;
            CleanButton.IsEnabled = RescanButton.IsEnabled = false;
            if (!_showResult)
            {
                Total.Text = "…";
                Summary.Text = "Считаем, сколько места занято…";
            }
            foreach (var vm in _items) vm.SizeValue = null;
            // каждый пункт считается в фоне отдельно: окно не ждёт самый медленный
            await Task.WhenAll(_items.Select(async vm =>
            {
                try { vm.SizeValue = await Task.Run(() => vm.Item.Scan()); }
                catch (Exception e) { vm.SizeValue = -1; if (vm.CanSelect) { vm.Error = true; vm.Result = "Не удалось посчитать: " + e.Message; } }
            }));
            _busy = false;
            RescanButton.IsEnabled = true;
            UpdateTotals();
        }

        void UpdateTotals()
        {
            if (_busy) return;
            long all = _items.Where(i => i.CanSelect).Sum(i => Math.Max(0, i.Size));
            long chosen = _items.Where(i => i.Checked && i.CanSelect).Sum(i => Math.Max(0, i.Size));
            CleanButton.IsEnabled = chosen > 0;
            if (_showResult) return;
            TotalCaption.Text = "Выбрано к очистке";
            Total.Text = Cleaner.Format(chosen);
            Summary.Text = $"Всего можно освободить {Cleaner.Format(all)}" + (_items.Any(i => !i.CanSelect) ? " (без пунктов, которым нужны права администратора)" : "");
        }

        void HideResult()
        {
            if (!_showResult) return;
            _showResult = false;
            Progress.Visibility = Visibility.Collapsed;
            Total.Foreground = (Brush)FindResource("Text");
        }

        async void OnRescan(object sender, RoutedEventArgs e)
        {
            HideResult();
            foreach (var vm in _items) vm.Result = null;
            await ScanAsync();
        }

        async void OnClean(object sender, RoutedEventArgs e)
        {
            var chosen = _items.Where(i => i.Checked && i.CanSelect && i.Size > 0).ToList();
            if (chosen.Count == 0) return;
            HideResult();
            _busy = true;
            CleanButton.IsEnabled = RescanButton.IsEnabled = false;
            CleanText.Text = "Чищу…";
            long total = 0, target = chosen.Sum(i => i.Size);
            CleanViewModel current = null;
            int step = 0;
            double shown = 0; // число в шапке догоняет реальное плавно: даже секундная очистка видна как отсчёт

            TotalCaption.Text = "Освобождено";
            Total.Text = Cleaner.Format(0);
            FillScale.ScaleX = 0;
            Fill.Background = (Brush)FindResource("Accent");
            Progress.Visibility = Visibility.Visible;
            CleanIcon.Text = "";
            Spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
            // 20 раз в секунду и только пока идёт очистка
            var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (s, _) =>
            {
                var cur = current;
                long now = cur == null ? 0 : Interlocked.Read(ref cur.Item.Done);
                long done = total + now;
                shown += (done - shown) * 0.2;
                if (done - shown < 1024 * 1024) shown = done;
                Total.Text = Cleaner.Format((long)shown);
                if (cur != null)
                {
                    Summary.Text = $"Чищу «{cur.Name}» — {step} из {chosen.Count}";
                    if (now > 0) cur.Result = $"Чищу… {Cleaner.Format(now)} из {Cleaner.Format(cur.Size)}";
                }
                FillScale.ScaleX = Math.Min(1, target > 0 ? shown / target : 0);
            };
            timer.Start();
            var started = DateTime.UtcNow;

            foreach (var vm in chosen) // по очереди: служба обновлений и корзина не любят параллельную работу
            {
                current = vm;
                step++;
                vm.Item.Done = 0;
                vm.Result = "Чищу…";
                vm.Error = false;
                try
                {
                    long freed = await Task.Run(() => vm.Item.Clean());
                    total += freed;
                    long left = Math.Max(0, vm.Size - freed);
                    vm.Result = $"Освобождено {Cleaner.Format(freed)}" + (left > 1024 * 1024 ? $". {Cleaner.Format(left)} заняты программами — останутся до следующей очистки." : ".")
                                + (vm.Item.Warning != null ? " " + vm.Item.Warning : "");
                }
                catch (Exception ex)
                {
                    App.Log(ex);
                    vm.Error = true;
                    vm.Result = "Ошибка: " + ex.Message;
                }
            }
            current = null;
            Summary.Text = "Почти готово…";
            // даём отсчёту дойти до конца и не обрываем анимацию раньше полутора секунд
            while (shown < total || DateTime.UtcNow - started < TimeSpan.FromSeconds(1.5)) await Task.Delay(50);
            timer.Stop();

            Spin.BeginAnimation(RotateTransform.AngleProperty, null);
            CleanIcon.Text = "";
            CleanText.Text = "Очистить выбранное";
            FillScale.ScaleX = 1;
            Fill.Background = (Brush)FindResource("Green");
            Total.Foreground = (Brush)FindResource("Green");
            Total.Text = Cleaner.Format(total);
            TotalCaption.Text = total > 0 ? "Готово! Освобождено" : "Готово";
            Summary.Text = FreeSpaceText();
            if (total > 0) TweakEngine.LogEntry("cleanup", $"Очистка диска: освобождено {Cleaner.Format(total)}", "clean");
            _showResult = true;
            _busy = false;
            await ScanAsync(); // размеры в списке обновятся, итог в шапке останется
        }

        static string FreeSpaceText()
        {
            try
            {
                var d = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
                return $"Свободно на диске {d.Name.TrimEnd('\\')} {Cleaner.Format(d.AvailableFreeSpace)}";
            }
            catch { return "Очистка завершена"; }
        }
    }
}
