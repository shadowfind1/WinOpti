using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinOpti.Cleanup;
using WinOpti.Startup;
using WinOpti.Sys;

namespace WinOpti.UI
{
    public partial class StartupPage : UserControl
    {
        readonly bool _admin = SysInfo.IsAdmin;
        List<StartupViewModel> _items = new List<StartupViewModel>();
        /// <summary>Что выключила кнопка «Выключить лишнее» — пока не нажато «Обновить», её можно отменить.</summary>
        List<StartupViewModel> _undo;
        bool _loaded, _busy;

        public StartupPage()
        {
            InitializeComponent();
            IsVisibleChanged += async (s, e) => { if (IsVisible && !_loaded) { _loaded = true; await LoadAsync(); } };
        }

        async Task LoadAsync()
        {
            _busy = true;
            _undo = null;
            DisableButton.IsEnabled = RefreshButton.IsEnabled = false;
            Total.Text = "…";
            Summary.Text = L.T("Смотрим, что запускается вместе с Windows…", "Looking at what starts with Windows…");
            List<StartupItem> items;
            string warning;
            try { (items, warning) = await Task.Run(() => StartupManager.List()); }
            catch (Exception e)
            {
                App.Log(e);
                (items, warning) = (new List<StartupItem>(), L.T("Не удалось прочитать автозагрузку: ", "Couldn’t read startup items: ") + e.Message);
            }
            _items = items.Select(i => new StartupViewModel(i, _admin)).ToList();
            foreach (var vm in _items) vm.StateChanged += UpdateTotals;
            // включённые и выключенные — раздельно; при переключении строка остаётся на месте, порядок обновится по «Обновить»
            OnList.ItemsSource = _items.Where(v => v.IsOn && !v.IsService).ToList();
            SvcList.ItemsSource = _items.Where(v => v.IsOn && v.IsService).ToList();
            OffList.ItemsSource = _items.Where(v => !v.IsOn).OrderBy(v => v.IsService).ToList();
            OnHeader.Visibility = _items.Any(v => v.IsOn && !v.IsService) ? Visibility.Visible : Visibility.Collapsed;
            SvcHeader.Visibility = _items.Any(v => v.IsOn && v.IsService) ? Visibility.Visible : Visibility.Collapsed;
            OffHeader.Visibility = _items.Any(v => !v.IsOn) ? Visibility.Visible : Visibility.Collapsed;
            AdminBanner.Visibility = !_admin && _items.Any(v => v.NeedsAdminVisible) ? Visibility.Visible : Visibility.Collapsed;
            Warning.Text = warning;
            Warning.Visibility = warning == null ? Visibility.Collapsed : Visibility.Visible;
            _busy = false;
            RefreshButton.IsEnabled = true;
            UpdateTotals();
            await Task.WhenAll(_items.Select(v => v.LoadIconAsync()));
        }

        IEnumerable<StartupViewModel> Recommended => _items.Where(v => v.CanDisable && v.IsOn && v.CanToggle);

        void UpdateTotals()
        {
            if (_busy) return;
            int on = _items.Count(v => v.IsOn && !v.IsService), svc = _items.Count(v => v.IsOn && v.IsService);
            Total.Text = L.N(on, "программа", "программы", "программ", "app", "apps");
            var running = _items.Where(v => v.IsOn && v.Running).ToList();
            int extra = Recommended.Count();
            var parts = new List<string>();
            if (svc > 0) parts.Add(L.T("плюс ", "plus ") + L.N(svc, "служба", "службы", "служб", "service", "services"));
            if (running.Count > 0) parts.Add(L.T($"сейчас занимают {Cleaner.Format(running.Sum(v => v.Item.RamBytes))} памяти", $"using {Cleaner.Format(running.Sum(v => v.Item.RamBytes))} of RAM now"));
            parts.Add(extra > 0 ? L.T($"без вреда можно выключить {extra}", $"{extra} can be safely turned off") : L.T("лишнего не нашли", "nothing extra found"));
            var text = string.Join(", ", parts) + ".";
            Summary.Text = char.ToUpper(text[0]) + text.Substring(1);
            if (_undo != null)
            {
                DisableText.Text = L.T("Вернуть как было", "Undo");
                DisableButton.IsEnabled = true;
            }
            else
            {
                DisableText.Text = extra > 0 ? L.T($"Выключить лишнее ({extra})", $"Turn off extras ({extra})") : L.T("Выключить лишнее", "Turn off extras");
                DisableButton.IsEnabled = extra > 0;
            }
        }

        async void OnDisableRecommended(object sender, RoutedEventArgs e)
        {
            bool undo = _undo != null;
            var targets = undo ? _undo : Recommended.ToList();
            _busy = true;
            DisableButton.IsEnabled = RefreshButton.IsEnabled = false;
            DisableText.Text = undo ? L.T("Возвращаю…", "Restoring…") : L.T("Выключаю…", "Turning off…");
            foreach (var vm in targets) await vm.SetAsync(undo); // по очереди: schtasks и реестр, быстро
            _undo = undo ? null : targets.Where(v => !v.IsOn).ToList();
            if (_undo != null && _undo.Count == 0) _undo = null;
            _busy = false;
            RefreshButton.IsEnabled = true;
            UpdateTotals();
        }

        async void OnRefresh(object sender, RoutedEventArgs e) => await LoadAsync();
    }
}
