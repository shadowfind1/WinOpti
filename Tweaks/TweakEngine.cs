using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using WinOpti.Sys;
using D = System.Collections.Generic.Dictionary<string, object>;

namespace WinOpti.Tweaks
{
    public enum TweakState { Off, On, Partial }

    /// <summary>
    /// Включение и откат твиков. Исходные значения и журнал — в %APPDATA%\winopt, как у WinOpti 1:
    /// что включено в одной версии, видно и откатывается в другой.
    /// </summary>
    public static class TweakEngine
    {
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "winopt");
        static string BackupFile => Path.Combine(DataDir, "backup.json");
        static string HistoryFile => Path.Combine(DataDir, "history.json");
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        static readonly object FileLock = new object();

        /// <summary>Состояние по реальной системе. Необязательные действия (optional) не считаются.</summary>
        public static TweakState GetState(Tweak t)
        {
            var flags = t.Actions.Where(a => !(a.TryGetValue("optional", out var o) && o is bool b && b)).Select(Actions.Detect).ToList();
            if (flags.All(f => f)) return TweakState.On;
            return flags.Any(f => f) ? TweakState.Partial : TweakState.Off;
        }

        static bool Optional(D a) => a.TryGetValue("optional", out var o) && o is bool b && b;

        static void Run(D a, Action act)
        {
            try { act(); }
            catch (Exception e) when (Optional(a) && (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException || e is InvalidOperationException)) { }
        }

        /// <summary>-> текст предупреждения или null.</summary>
        public static string Apply(Tweak t)
        {
            lock (FileLock)
            {
                var backup = LoadBackup();
                var snaps = backup.TryGetValue(t.Id, out var s) && s is IEnumerable e ? e.Cast<object>().ToList() : new List<object>();
                // исходное состояние пишем один раз; если в tweaks.json дописали действия — снимаем только их
                if (snaps.Count < t.Actions.Count)
                {
                    for (int i = snaps.Count; i < t.Actions.Count; i++) snaps.Add(Actions.Snapshot(t.Actions[i]));
                    backup[t.Id] = snaps;
                    SaveBackup(backup);
                }
            }
            foreach (var a in t.Actions) Run(a, () => Actions.Apply(a));
            return Join(Refreshers.Run(t), Log(t, "on"));
        }

        public static string Revert(Tweak t)
        {
            List<object> snaps;
            D backup;
            lock (FileLock)
            {
                backup = LoadBackup();
                snaps = backup.TryGetValue(t.Id, out var s) && s is IEnumerable e ? e.Cast<object>().ToList() : new List<object>();
            }
            while (snaps.Count < t.Actions.Count) snaps.Add(null); // для новых действий — default из JSON
            for (int i = 0; i < t.Actions.Count; i++)
            {
                var a = t.Actions[i];
                var snap = snaps[i];
                Run(a, () => Actions.Restore(a, snap));
            }
            lock (FileLock)
            {
                backup = LoadBackup();
                if (backup.Remove(t.Id)) SaveBackup(backup);
            }
            return Join(Refreshers.Run(t), Log(t, "off"));
        }

        /// <summary>Твики, включённые через WinOpti (любой версии) и ещё не откаченные.</summary>
        public static HashSet<string> Applied() { lock (FileLock) return new HashSet<string>(LoadBackup().Keys); }

        static string Join(params string[] w) { var s = string.Join("; ", w.Where(x => !string.IsNullOrEmpty(x))); return s.Length == 0 ? null : s; }

        // ---------- файлы ----------

        static D LoadBackup()
        {
            try { return Json.DeserializeObject(File.ReadAllText(BackupFile)) as D ?? new D(); }
            catch { return new D(); }
        }

        static void SaveBackup(D data) => WriteJson(BackupFile, data);

        /// <summary>Через временный файл: другая версия WinOpti может читать его одновременно.</summary>
        static void WriteJson(string path, object data)
        {
            Directory.CreateDirectory(DataDir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(data));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }

        static string Log(Tweak t, string action) => LogEntry(t.Id, t.Name, action);

        /// <summary>Запись в общий журнал WinOpti (тот же history.json, что у WinOpti 1). -> текст предупреждения или null.</summary>
        public static string LogEntry(string id, string name, string action)
        {
            try
            {
                lock (FileLock)
                {
                    List<object> history;
                    try { history = (Json.DeserializeObject(File.ReadAllText(HistoryFile)) as IEnumerable)?.Cast<object>().ToList() ?? new List<object>(); }
                    catch { history = new List<object>(); }
                    history.Add(new D { ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), ["id"] = id, ["name"] = name, ["action"] = action });
                    WriteJson(HistoryFile, history.Skip(Math.Max(0, history.Count - 200)).ToList());
                }
                return null;
            }
            catch (Exception e) { return "журнал не записан: " + e.Message; }
        }

        /// <summary>Журнал WinOpti (общий с версией 1), новые сверху.</summary>
        public static List<(string time, string id, string name, string action)> History()
        {
            lock (FileLock)
            {
                try
                {
                    return ((Json.DeserializeObject(File.ReadAllText(HistoryFile)) as IEnumerable)?.Cast<object>() ?? Enumerable.Empty<object>())
                        .OfType<D>()
                        .Select(d => (Str(d, "time"), Str(d, "id"), Str(d, "name"), Str(d, "action")))
                        .Reverse().ToList();
                }
                catch { return new List<(string, string, string, string)>(); }
            }
        }

        static string Str(D d, string k) => d.TryGetValue(k, out var v) ? v as string ?? "" : "";

        // ---------- точка восстановления ----------

        const string SrKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
        static bool _restoreChecked;

        /// <summary>
        /// Точка восстановления перед первым изменением за сессию, если свежей (моложе суток) ещё нет:
        /// создание точки — снимок диска, на это время Windows тормозит, поэтому не чаще раза в сутки.
        /// -> null или текст, почему точку создать не вышло (твик при этом всё равно включается).
        /// </summary>
        public static string EnsureRestorePoint()
        {
            if (_restoreChecked) return null;
            _restoreChecked = true;
            if (!ProtectionOn) return "точка восстановления не создана: защита системы выключена";
            var (code, newest) = SysInfo.Run("powershell", "-NoProfile -Command \"$p = Get-ComputerRestorePoint | Sort-Object SequenceNumber | Select-Object -Last 1; if ($p) { $p.ConvertToDateTime($p.CreationTime).ToString('o') }\"");
            if (code == 0 && DateTime.TryParse(newest.Trim(), null, DateTimeStyles.RoundtripKind, out var when) && DateTime.Now - when < TimeSpan.FromHours(24))
                return null;
            var err = CreateRestorePoint("WinOpti");
            return err == null ? null : "точка восстановления не создана: " + err;
        }

        /// <summary>Включена ли защита системы (без неё Windows точки не создаёт).</summary>
        public static bool ProtectionOn => Reg.GetInt(RegistryHive.LocalMachine, SrKey, "RPSessionInterval") == 1;

        /// <summary>Создать точку восстановления сейчас. -> null или текст ошибки.</summary>
        public static string CreateRestorePoint(string description)
        {
            // Windows создаёт не больше одной точки в сутки и лишние молча пропускает — снимаем ограничение на время создания
            var old = Reg.Get(RegistryHive.LocalMachine, SrKey, "SystemRestorePointCreationFrequency");
            try { Reg.SetDword(RegistryHive.LocalMachine, SrKey, "SystemRestorePointCreationFrequency", 0); } catch { }
            try
            {
                var (rc, output) = SysInfo.Run("powershell", $"-NoProfile -Command \"$ErrorActionPreference='Stop'; Checkpoint-Computer -Description '{description.Replace("'", "''")}' -RestorePointType MODIFY_SETTINGS\"");
                return rc == 0 ? null : output.Trim().Split('\n')[0];
            }
            finally
            {
                try
                {
                    if (old is int o) Reg.SetDword(RegistryHive.LocalMachine, SrKey, "SystemRestorePointCreationFrequency", o);
                    else using (var k = Reg.Root(RegistryHive.LocalMachine).OpenSubKey(SrKey, true)) k?.DeleteValue("SystemRestorePointCreationFrequency", false);
                }
                catch { }
            }
        }
    }
}
