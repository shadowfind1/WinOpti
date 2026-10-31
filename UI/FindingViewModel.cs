using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using WinOpti.Diagnostics;
using WinOpti.Sys;

namespace WinOpti.UI
{
    public sealed class RelayCommand : ICommand
    {
        readonly Action _run;
        public RelayCommand(Action run) { _run = run; }
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object p) => true;
        public void Execute(object p) => _run();
    }

    /// <summary>Карточка находки на экране: состояние кнопки «Исправить», инструкция, результат.</summary>
    public sealed class FindingViewModel : INotifyPropertyChanged
    {
        public Finding F { get; }
        public event PropertyChangedEventHandler PropertyChanged;
        public event Action Fixed;

        public FindingViewModel(Finding f)
        {
            F = f;
            FixCommand = new RelayCommand(async () => await FixAsync());
            GuideCommand = new RelayCommand(() => GuideOpen = !GuideOpen);
        }

        public string Title => F.Title;
        public string Detail => F.Detail;
        public string Impact => F.Impact;
        public bool HasImpact => !string.IsNullOrEmpty(F.Impact) && F.Severity != Severity.Ok;
        public string Guide => F.Guide;
        public bool HasGuide => !string.IsNullOrEmpty(F.Guide);
        public string FixLabel => F.FixLabel;
        public bool IsOk => F.Severity == Severity.Ok;
        public int Order => -(int)F.Severity;

        public string Glyph => F.Severity switch
        {
            Severity.Critical => "",
            Severity.Warn => "",
            Severity.Info => "",
            _ => "",
        };

        static SolidColorBrush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex); b.Freeze(); return b; }
        static readonly SolidColorBrush red = B("#FF5D6C"), orange = B("#FFB347"), blue = B("#8FA8FF"), green = B("#4ADE80");
        static readonly SolidColorBrush redSoft = B("#2A121A"), orangeSoft = B("#2A1F10"), blueSoft = B("#131C2E"), greenSoft = B("#0E2419");

        public Brush Tint => F.Severity switch { Severity.Critical => red, Severity.Warn => orange, Severity.Info => blue, _ => green };
        public Brush TintSoft => F.Severity switch { Severity.Critical => redSoft, Severity.Warn => orangeSoft, Severity.Info => blueSoft, _ => greenSoft };
        public string SeverityText => F.Severity switch { Severity.Critical => L.T("Важно", "Important"), Severity.Warn => L.T("Стоит исправить", "Worth fixing"), Severity.Info => L.T("Совет", "Tip"), _ => L.T("В порядке", "All good") };

        bool _busy, _done, _guideOpen;
        string _result;
        bool _error;

        public bool CanFix => F.Fix != null && !_done && !_busy;
        public bool ShowGuideButton => HasGuide && !_done;
        public bool Busy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(CanFix)); } }
        public bool Done { get => _done; private set { _done = value; Changed(); Changed(nameof(CanFix)); Changed(nameof(ShowGuideButton)); } }
        public bool GuideOpen { get => _guideOpen; set { _guideOpen = value; Changed(); Changed(nameof(GuideButtonText)); } }
        public string GuideButtonText => _guideOpen ? L.T("Скрыть", "Hide") : (F.Fix == null ? L.T("Как исправить", "How to fix") : L.T("Подробнее", "Details"));
        public string Result { get => _result; private set { _result = value; Changed(); Changed(nameof(HasResult)); } }
        public bool HasResult => !string.IsNullOrEmpty(_result);
        public Brush ResultBrush => _error ? red : green;
        public string NeedsRebootText => F.FixNeedsReboot ? L.T("нужна перезагрузка", "restart needed") : null;
        public bool NeedsReboot => F.FixNeedsReboot && !_done;

        public ICommand FixCommand { get; }
        public ICommand GuideCommand { get; }

        async Task FixAsync()
        {
            if (!CanFix) return;
            if (F.FixNeedsAdmin && !SysInfo.IsAdmin)
            {
                _error = true;
                Result = L.T("Нужны права администратора: запусти WinOpti от имени администратора.", "Administrator rights needed: run WinOpti as administrator.");
                Changed(nameof(ResultBrush));
                return;
            }
            Busy = true;
            try
            {
                var text = await Task.Run(() => F.Fix());
                _error = false;
                Result = text;
                Done = true;
                Fixed?.Invoke();
            }
            catch (Exception e)
            {
                _error = true;
                Result = e is UnauthorizedAccessException || e is System.Security.SecurityException
                    ? L.T("Windows не дала изменить настройку: нужны права администратора.", "Windows blocked the change: administrator rights needed.")
                    : e.Message;
            }
            finally
            {
                Changed(nameof(ResultBrush));
                Busy = false;
            }
        }

        void Changed([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
