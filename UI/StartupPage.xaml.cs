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
            Summary.Text = "Смотрим, что запускается вместе с Windows…";
            List<StartupItem> items;
            string warning;
            try { (items, warning) = await Task.Run(() => StartupManager.List()); }
            catch (Exception e)
            {
                App.Log(e);
                (items, warning) = (new List<StartupItem>(), "Не удалось прочитать автозагрузку: " + e.Message);
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
            Total.Text = $"{on} {Plural(on, "программа", "программы", "программ")}";
            var running = _items.Where(v => v.IsOn && v.Running).ToList();
            int extra = Recommended.Count();
            var parts = new List<string>();
            if (svc > 0) parts.Add($"плюс {svc} {Plural(svc, "служба", "службы", "служб")}");
            if (running.Count > 0) parts.Add($"сейчас занимают {Cleaner.Format(running.Sum(v => v.Item.RamBytes))} памяти");
            parts.Add(extra > 0 ? $"без вреда можно выключить {extra}" : "лишнего не нашли");
            var text = string.Join(", ", parts) + ".";
            Summary.Text = char.ToUpper(text[0]) + text.Substring(1);
            if (_undo != null)
            {
                DisableText.Text = "Вернуть как было";
                DisableButton.IsEnabled = true;
            }
            else
            {
                DisableText.Text = extra > 0 ? $"Выключить лишнее ({extra})" : "Выключить лишнее";
                DisableButton.IsEnabled = extra > 0;
            }
        }

        static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            return m10 == 1 && m100 != 11 ? one : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? few : many;
        }

        async void OnDisableRecommended(object sender, RoutedEventArgs e)
        {
            bool undo = _undo != null;
            var targets = undo ? _undo : Recommended.ToList();
            _busy = true;
            DisableButton.IsEnabled = RefreshButton.IsEnabled = false;
            DisableText.Text = undo ? "Возвращаю…" : "Выключаю…";
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
