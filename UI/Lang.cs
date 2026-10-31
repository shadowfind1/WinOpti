using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;

namespace WinOpti
{
    /// <summary>
    /// Язык интерфейса. Русский и английский текст лежат рядом в коде: L.T("Очистка", "Cleanup").
    /// Выбор хранится в lang.txt; по умолчанию — по языку Windows. Смена языка перезапускает окно.
    /// </summary>
    public static class L
    {
        static readonly string FilePath = Path.Combine(App.DataDir, "lang.txt");
        static readonly string[] RuCultures = { "ru", "uk", "be", "kk", "ky", "uz", "tg", "hy", "az", "ka", "tk" };

        public static bool En { get; private set; }

        public static string T(string ru, string en) => En ? en : ru;

        /// <summary>«5 программ» / «5 apps»: русские три формы и английские две.</summary>
        public static string N(int n, string ru1, string ru2, string ru5, string en1, string enN, bool withNumber = true)
        {
            string word;
            if (En) word = n == 1 ? en1 : enN;
            else
            {
                int m10 = n % 10, m100 = n % 100;
                word = m10 == 1 && m100 != 11 ? ru1 : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? ru2 : ru5;
            }
            return withNumber ? $"{n} {word}" : word;
        }

        public static void Init(string[] args)
        {
            var forced = args.FirstOrDefault(a => a.StartsWith("--lang="));
            if (forced != null) { En = forced.Substring(7) == "en"; return; }
            try
            {
                if (File.Exists(FilePath)) { En = File.ReadAllText(FilePath).Trim() == "en"; return; }
            }
            catch { }
            En = !SpeaksRussian();
        }

        // У многих русскоязычных игроков Windows на английском, но русская раскладка есть почти всегда.
        static bool SpeaksRussian()
        {
            bool Ru(CultureInfo c) => c != null && RuCultures.Contains(c.TwoLetterISOLanguageName);
            if (Ru(CultureInfo.CurrentUICulture) || Ru(CultureInfo.CurrentCulture)) return true;
            try
            {
                var layouts = System.Windows.Input.InputLanguageManager.Current?.AvailableInputLanguages;
                if (layouts != null && layouts.Cast<CultureInfo>().Any(Ru)) return true;
            }
            catch { }
            return false;
        }

        /// <summary>Запоминает язык и перезапускает программу с теми же аргументами.</summary>
        public static void Switch(bool en)
        {
            if (en == En) return;
            try
            {
                Directory.CreateDirectory(App.DataDir);
                File.WriteAllText(FilePath, en ? "en" : "ru");
            }
            catch { }
            var args = Environment.GetCommandLineArgs().Skip(1).Where(a => !a.StartsWith("--lang="))
                .Select(a => a.Contains(' ') ? "\"" + a + "\"" : a);
            try
            {
                Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, string.Join(" ", args)) { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception e) { App.Log(e); }
        }
    }

    namespace UI
    {
        /// <summary>Текст в XAML: Text="{u:Tr 'Очистка диска', En='Disk cleanup'}".</summary>
        [MarkupExtensionReturnType(typeof(string))]
        public sealed class Tr : MarkupExtension
        {
            public Tr() { }
            public Tr(string ru) { Ru = ru; }
            [ConstructorArgument("ru")] public string Ru { get; set; }
            public string En { get; set; }
            public override object ProvideValue(IServiceProvider sp) => L.En && En != null ? En : Ru;
        }
    }
}
