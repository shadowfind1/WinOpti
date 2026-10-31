using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using WinOpti.Sys;

namespace WinOpti.UI
{
    public partial class ReportWindow : Window
    {
        static bool _open; // ошибки могут сыпаться подряд — одного окна достаточно

        readonly string _report;

        ReportWindow(string heading, string report)
        {
            InitializeComponent();
            Heading.Text = heading;
            _report = report;
            ReportText.Text = report;
            MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        }

        /// <summary>Ошибка прямо сейчас: программа продолжает работать.</summary>
        public static void ShowFor(Exception e) =>
            Show(L.T("Что-то пошло не так", "Something went wrong") + ": " + e.Message, CrashReport.Build(e));

        /// <summary>Отчёт о падении, сохранённый в прошлый запуск.</summary>
        public static void ShowPending(string report) =>
            Show(L.T("В прошлый раз WinOpti закрылась из-за ошибки", "Last time WinOpti closed because of an error"), report);

        static void Show(string heading, string report)
        {
            if (_open) return;
            _open = true;
            try
            {
                var owner = Application.Current?.MainWindow;
                var w = new ReportWindow(heading, report);
                if (owner != null && owner.IsVisible && owner != w) w.Owner = owner;
                else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                w.ShowDialog();
            }
            finally { _open = false; }
        }

        void OnCopy(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_report);
                CopyText.Text = L.T("Скопировано", "Copied");
            }
            catch (Exception ex) { App.Log(ex); } // буфер обмена бывает занят другой программой
        }

        void OnGitHub(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(CrashReport.IssueUrl(_report)) { UseShellExecute = true }); }
            catch (Exception ex) { App.Log(ex); }
        }

        void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
