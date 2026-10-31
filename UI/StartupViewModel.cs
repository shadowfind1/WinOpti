using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinOpti.Cleanup;
using WinOpti.Startup;

namespace WinOpti.UI
{
    /// <summary>Строка автозагрузки: иконка, подсказка, что занимает сейчас, переключатель.</summary>
    public sealed class StartupViewModel : INotifyPropertyChanged
    {
        public StartupItem Item { get; }
        public event PropertyChangedEventHandler PropertyChanged;
        public event Action StateChanged;
        readonly bool _admin;
        bool _busy, _error;
        string _message;
        ImageSource _icon;

        public StartupViewModel(StartupItem item, bool admin)
        {
            Item = item;
            _admin = admin;
            ToggleCommand = new RelayCommand(async () => await SetAsync(!Item.Enabled));
        }

        public string Name => Item.Name;
        public string Details => string.Join(" · ", new[] { Item.Company, Item.Where }.Where(s => !string.IsNullOrWhiteSpace(s)));
        public bool IsOn => Item.Enabled;
        public bool CanToggle => (_admin || !Item.Admin) && !Item.Locked && !_busy;
        public bool NeedsAdminVisible => Item.Admin && !_admin;
        public bool Busy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(CanToggle)); } }
        public bool Missing => Item.Missing;
        public bool CanDisable => Item.Hint == "disable";
        public bool ShouldKeep => Item.Hint == "keep";
        public bool HasHint => Item.HintText != null;
        public string HintText => Item.HintText == null ? null : char.ToUpper(Item.HintText[0]) + Item.HintText.Substring(1) + ".";
        public bool Running => Item.Processes > 0;
        public string Usage => !Running ? null
            : $"Сейчас занимает {Cleaner.Format(Item.RamBytes)} памяти"
              + (Item.Processes > 1 ? $" · {Item.Processes} {Plural(Item.Processes, "процесс", "процесса", "процессов")}" : "")
              + (Item.CpuTime.TotalSeconds >= 10 ? $" · {CpuText(Item.CpuTime)} процессора с момента запуска" : "");
        public string Message { get => _message; private set { _message = value; Changed(); Changed(nameof(HasMessage)); Changed(nameof(MessageBrush)); } }
        public bool HasMessage => !string.IsNullOrEmpty(_message);
        public Brush MessageBrush => (Brush)Application.Current.FindResource(_error ? "Red" : "Muted");
        public ICommand ToggleCommand { get; }

        public bool IsService => Item.Source == Source.Service;
        /// <summary>Значок, пока иконки нет или если у программы её нет вовсе: шестерёнка у служб, окно у программ.</summary>
        public string Glyph => IsService ? "" : "";
        public ImageSource Icon => _icon;
        public bool HasIcon => _icon != null;

        static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            return m10 == 1 && m100 != 11 ? one : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? few : many;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        static ImageSource PackageLogo(string folder)
        {
            var files = Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories);
            // маленький квадратный логотип без подложки, иначе любой квадратный, иначе логотип магазина
            var best = files.FirstOrDefault(f => Regex.IsMatch(Path.GetFileName(f), @"(Square44x44Logo|AppList).*targetsize-32(_altform-unplated)?\.png$", RegexOptions.IgnoreCase))
                       ?? files.FirstOrDefault(f => Regex.IsMatch(Path.GetFileName(f), @"(Square44x44Logo|AppList).*targetsize-(24|48)", RegexOptions.IgnoreCase))
                       ?? files.FirstOrDefault(f => Regex.IsMatch(Path.GetFileName(f), @"(Square44x44Logo|AppList|StoreLogo)", RegexOptions.IgnoreCase));
            if (best == null) return null;
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.DecodePixelWidth = 64;
            img.UriSource = new Uri(best);
            img.EndInit();
            img.Freeze();
            return img;
        }

        static string CpuText(TimeSpan t) => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} мин" : $"{(int)t.TotalSeconds} с";

        static readonly ConcurrentDictionary<string, ImageSource> IconCache = new ConcurrentDictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Иконка из exe — в фоне, готовая (замороженная) картинка передаётся в окно.</summary>
        public async Task LoadIconAsync()
        {
            var path = Item.Target;
            if (string.IsNullOrEmpty(path)) return;
            _icon = await Task.Run(() => IconCache.GetOrAdd(path, p =>
            {
                try
                {
                    // приложение из Store: иконка — png из папки пакета
                    if (Directory.Exists(p)) return PackageLogo(p);
                    if (!File.Exists(p)) return null;
                    // ExtractIconEx, а не ExtractAssociatedIcon: у exe без иконки вернёт 0, а не значок «пустой файл»
                    var large = new IntPtr[1];
                    if (ExtractIconEx(p, 0, large, null, 1) == 0 || large[0] == IntPtr.Zero) return null;
                    try
                    {
                        var src = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        src.Freeze();
                        return src;
                    }
                    finally { DestroyIcon(large[0]); }
                }
                catch { return null; }
            }));
            Changed(nameof(Icon));
            Changed(nameof(HasIcon));
        }

        public async Task SetAsync(bool enable)
        {
            if (!CanToggle || Item.Enabled == enable) return;
            Busy = true;
            _error = false;
            Message = null;
            try
            {
                await Task.Run(() => StartupManager.SetEnabled(Item, enable));
                Item.Enabled = await Task.Run(() => StartupManager.IsEnabled(Item));
                if (!enable && Running) Message = "Выключено. Сейчас программа ещё работает — со следующего включения ПК запускаться не будет.";
            }
            catch (Exception e)
            {
                App.Log(e);
                _error = true;
                Message = "Не получилось: " + e.Message;
            }
            Changed(nameof(IsOn));
            Busy = false;
            StateChanged?.Invoke();
        }

        void Changed([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
