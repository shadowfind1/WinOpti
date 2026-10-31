using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WinOpti.Sys;
using WinOpti.Tweaks;

namespace WinOpti.UI
{
    public partial class TweaksPage : UserControl
    {
        internal static readonly (string id, string name)[] Cats =
        {
            ("all", "Все"), ("privacy", "Телеметрия и фон"), ("interface", "Интерфейс"),
            ("performance", "Производительность"), ("gaming", "Игры и ввод"), ("system", "Система"),
        };

        readonly List<TweakViewModel> _items;
        readonly ICollectionView _view;
        string _category = "all";
        bool _loaded;

        public TweaksPage()
        {
            InitializeComponent();
            bool admin = SysInfo.IsAdmin;
            AdminBanner.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
            _items = Tweak.LoadAll().Where(t => !t.Hidden).Select(t => new TweakViewModel(t, admin) { BeforeApply = RestorePointAsync }).ToList();
            foreach (var vm in _items) vm.StateChanged += UpdateStatus;
            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = o => o is TweakViewModel vm && (_category == "all" || vm.T.Category == _category) && vm.Matches(Search.Text);
            List.ItemsSource = _view;
            List.RequestBringIntoView += (s, e) => e.Handled = true;

            foreach (var (id, name) in Cats)
            {
                var rb = new RadioButton { Content = name, Tag = id, GroupName = "cat", Style = (Style)FindResource("FilterPill"), IsChecked = id == "all" };
                rb.Checked += (s, e) => { _category = (string)((RadioButton)s).Tag; Refilter(); };
                Categories.Children.Add(rb);
            }
            IsVisibleChanged += async (s, e) => { if (IsVisible && !_loaded) { _loaded = true; await LoadAsync(); } };
        }

        /// <summary>Состояние читаем в фоне по 4 твика сразу: задачи и службы опрашиваются внешними утилитами, окно не должно ждать.</summary>
        async Task LoadAsync()
        {
            Status.Text = "Проверяем, что уже включено…";
            var gate = new SemaphoreSlim(4);
            await Task.WhenAll(_items.Select(async vm =>
            {
                await gate.WaitAsync();
                try { await vm.LoadAsync(); } finally { gate.Release(); }
            }));
            UpdateStatus();
        }

        void UpdateStatus()
        {
            var supported = _items.Where(i => i.T.Supported).ToList();
            int on = supported.Count(i => i.IsOn);
            Status.Text = $"Включено {on} из {supported.Count}" + (supported.Any(i => i.IsPartial) ? $" · частично: {supported.Count(i => i.IsPartial)}" : "");
        }

        void OnSearch(object sender, TextChangedEventArgs e) => Refilter();

        void Refilter()
        {
            _view?.Refresh();
            Empty.Visibility = _view != null && !_view.Cast<object>().Any() ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Точка восстановления перед первым включением за сессию; пока создаётся — пишем об этом.</summary>
        async Task<string> RestorePointAsync()
        {
            var old = Status.Text;
            Status.Text = "Создаю точку восстановления Windows перед первым изменением — это до минуты…";
            try { return await Task.Run(TweakEngine.EnsureRestorePoint); }
            finally { Status.Text = old; UpdateStatus(); }
        }
    }
}
