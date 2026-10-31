using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace WinOpti
{
    public partial class App : Application
    {
        public static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinOpti");

        protected override void OnStartup(StartupEventArgs e)
        {
            // Ошибка не должна закрывать программу молча: пишем в журнал и показываем.
            DispatcherUnhandledException += OnUiError;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log(a.ExceptionObject as Exception);
            UI.FpsPage.Source = new Fps.PresentMonSource();
            base.OnStartup(e);
            if (e.Args.Contains("--gamemode"))
            {
                // авто-игровой режим: без окна, только фоновый поток
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                new System.Threading.Thread(() => { try { Game.GameMode.Run(); } catch (Exception ex) { Log(ex); } Dispatcher.Invoke(Shutdown); })
                    { IsBackground = true, Name = "WinOpti game mode" }.Start();
                return;
            }
            MainWindow = new MainWindow();
            MainWindow.Show();
        }

        void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log(e.Exception);
            MessageBox.Show("Что-то пошло не так: " + e.Exception.Message + "\n\nПодробности в " + Path.Combine(DataDir, "crash.log"),
                "WinOpti", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        public static void Log(Exception e)
        {
            if (e == null) return;
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(Path.Combine(DataDir, "crash.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e}\n\n");
            }
            catch { }
        }
    }
}
