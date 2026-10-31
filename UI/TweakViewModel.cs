using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using WinOpti.Tweaks;

namespace WinOpti.UI
{
    /// <summary>Карточка твика: состояние, переключатель, результат последнего действия.</summary>
    public sealed class TweakViewModel : INotifyPropertyChanged
    {
        public Tweak T { get; }
        public event PropertyChangedEventHandler PropertyChanged;
        /// <summary>Перед первым включением за сессию — точка восстановления (её создаёт страница, показывая статус).</summary>
        public Func<Task<string>> BeforeApply;
        public event Action StateChanged;

        public TweakViewModel(Tweak t, bool admin)
        {
            T = t;
            _admin = admin;
            ToggleCommand = new RelayCommand(async () => await ToggleAsync());
        }

        readonly bool _admin;
        bool _loaded, _busy, _error;
        TweakState _state;
        string _message;

        public string Name => T.Name;
        public string Description => T.Description;
        public string After => string.IsNullOrEmpty(T.After) ? null : "После включения: " + T.After.TrimEnd('.').ToLowerInvariant() + ".";
        public bool HasAfter => !string.IsNullOrEmpty(T.After);
        public string RiskText => T.Risk switch { "high" => "высокий риск", "medium" => "средний риск", _ => "низкий риск" };
        static readonly Brush Green = Freeze("#3ECF8E"), Orange = Freeze("#FFB547"), Red = Freeze("#FF5D6C");
        static readonly Brush GreenSoft = Freeze("#10231A"), OrangeSoft = Freeze("#2A2112"), RedSoft = Freeze("#2A1519");
        static Brush Freeze(string hex) { var b = (Brush)new BrushConverter().ConvertFrom(hex); b.Freeze(); return b; }
        public Brush RiskBrush => T.Risk switch { "high" => Red, "medium" => Orange, _ => Green };
        public Brush RiskSoft => T.Risk switch { "high" => RedSoft, "medium" => OrangeSoft, _ => GreenSoft };
        public bool Unsupported => !T.Supported;
        public string UnsupportedText => "только Windows " + string.Join(" и ", T.Windows);

        public TweakState State { get => _state; private set { _state = value; Changed(); Changed(nameof(IsOn)); Changed(nameof(IsPartial)); } }
        public bool IsOn => _state == TweakState.On;
        public bool IsPartial => _state == TweakState.Partial;
        public bool Loaded { get => _loaded; private set { _loaded = value; Changed(); Changed(nameof(CanToggle)); } }
        public bool Busy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(CanToggle)); } }
        public bool CanToggle => _admin && T.Supported && _loaded && !_busy;
        public string Message { get => _message; private set { _message = value; Changed(); Changed(nameof(HasMessage)); Changed(nameof(MessageBrush)); } }
        public bool HasMessage => !string.IsNullOrEmpty(_message);
        public Brush MessageBrush => _error ? Red : Freeze("#8B9099");
        public ICommand ToggleCommand { get; }

        /// <summary>Поиск по названию, описанию и ключевым словам.</summary>
        public bool Matches(string q) => string.IsNullOrWhiteSpace(q)
            || (T.Name + " " + T.Description + " " + T.Keywords + " " + T.NameEn).IndexOf(q.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

        public async Task LoadAsync()
        {
            if (!T.Supported) { Loaded = true; return; }
            try { State = await Task.Run(() => TweakEngine.GetState(T)); }
            catch (Exception e) { App.Log(e); _error = true; Message = "Не удалось прочитать состояние: " + e.Message; }
            Loaded = true;
        }

        async Task ToggleAsync()
        {
            if (!CanToggle) return;
            bool turnOn = _state != TweakState.On; // «частично» → включаем до конца
            Busy = true;
            _error = false;
            Message = null;
            try
            {
                string pre = turnOn && BeforeApply != null ? await BeforeApply() : null;
                string warn = await Task.Run(() => turnOn ? TweakEngine.Apply(T) : TweakEngine.Revert(T));
                State = await Task.Run(() => TweakEngine.GetState(T));
                var parts = new[] { pre, warn, turnOn && HasAfter ? "Готово. " + T.After.TrimEnd('.') + "." : null };
                Message = string.Join(" ", Array.FindAll(parts, p => !string.IsNullOrEmpty(p)));
            }
            catch (Exception e)
            {
                App.Log(e);
                _error = true;
                Message = e is UnauthorizedAccessException || e is System.Security.SecurityException
                    ? "Windows не дала изменить настройку: нужны права администратора."
                    : "Ошибка: " + e.Message;
                try { State = await Task.Run(() => TweakEngine.GetState(T)); } catch { }
            }
            finally
            {
                Busy = false;
                StateChanged?.Invoke();
            }
        }

        void Changed([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
