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
            AppDomain.CurrentDomain.UnhandledException += (s, a) => SavePendingCrash(a.ExceptionObject as Exception);
            L.Init(e.Args);
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
            OfferPendingCrash();
#if DEBUG
            // проверка окна ошибки: --test-crash=ui (программа живёт дальше) или --test-crash=fatal (падает, отчёт при следующем запуске)
            if (e.Args.Contains("--test-crash=ui"))
                Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("Test UI error")), DispatcherPriority.ApplicationIdle);
            if (e.Args.Contains("--test-crash=fatal"))
                new System.Threading.Thread(() => throw new InvalidOperationException("Test fatal error")).Start();
#endif
        }

        void OnUiError(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log(e.Exception);
            e.Handled = true;
            try { UI.ReportWindow.ShowFor(e.Exception); } catch (Exception ex) { Log(ex); }
        }

        /// <summary>Программа падает целиком: окно показать уже нельзя, поэтому сохраняем отчёт и предложим отправить при следующем запуске.</summary>
        static void SavePendingCrash(Exception e)
        {
            Log(e);
            try { File.WriteAllText(Sys.CrashReport.PendingFile, Sys.CrashReport.Build(e)); } catch { }
        }

        void OfferPendingCrash()
        {
            try
            {
                if (!File.Exists(Sys.CrashReport.PendingFile)) return;
                var report = File.ReadAllText(Sys.CrashReport.PendingFile);
                File.Delete(Sys.CrashReport.PendingFile); // предлагаем один раз
                Dispatcher.BeginInvoke(new Action(() => UI.ReportWindow.ShowPending(report)), DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex) { Log(ex); }
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
