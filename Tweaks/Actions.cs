using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;
using WinOpti.Sys;
using D = System.Collections.Generic.Dictionary<string, object>;

namespace WinOpti.Tweaks
{
    /// <summary>
    /// Действия твиков — порт engine.py из WinOpti 1 один в один. У каждого типа четыре операции:
    /// Detect (применено ли сейчас), Snapshot (исходное состояние для отката), Apply, Restore(snap; null → default).
    /// Снимки пишутся в тот же backup.json и в том же формате, что у WinOpti 1.
    /// </summary>
    public static class Actions
    {
        public static bool Detect(D a) => Type(a) switch
        {
            "registry" => RegistryDetect(a),
            "registry_key" => RegKeyDetect(a),
            "service" => ServiceDetect(a),
            "task" => TaskDetect(a),
            "command" => CommandDetect(a),
            "power_scheme" => PowerDetect(a),
            _ => throw new NotSupportedException(Type(a)),
        };

        public static object Snapshot(D a) => Type(a) switch
        {
            "registry" => RegistrySnapshot(a),
            "registry_key" => new D { ["existed"] = KeyExists(a), ["default"] = Read(Hive(a), Tweak.Str(a, "path"), "") },
            "service" => new D { ["mode"] = ServiceMode(Tweak.Str(a, "name")) },
            "task" => new D { ["state"] = TaskState(Tweak.Str(a, "path")) },
            "command" => null,
            "power_scheme" => new D { ["scheme"] = ActiveScheme() },
            _ => throw new NotSupportedException(Type(a)),
        };

        public static void Apply(D a)
        {
            switch (Type(a))
            {
                case "registry": Write(Hive(a), Tweak.Str(a, "path"), Tweak.Str(a, "name"), a["kind"], a["value"]); break;
                case "registry_key": Write(Hive(a), Tweak.Str(a, "path"), "", "SZ", Tweak.Str(a, "value") ?? ""); break;
                case "service": ServiceApply(a); break;
                case "task": if (TaskState(Tweak.Str(a, "path")) != "missing") TaskSet(Tweak.Str(a, "path"), false); break;
                case "command": Command(Tweak.Str(a, "apply")); break;
                case "power_scheme": PowerApply(a); break;
                default: throw new NotSupportedException(Type(a));
            }
        }

        public static void Restore(D a, object snap)
        {
            var s = snap as D;
            switch (Type(a))
            {
                case "registry": RegistryRestore(a, s); break;
                case "registry_key": RegKeyRestore(a, s); break;
                case "service":
                    var mode = s != null ? s.Get("mode") as string : Tweak.Str(a, "default");
                    if (mode != null) ServiceSet(Tweak.Str(a, "name"), mode);
                    break;
                case "task":
                    var state = s != null ? s.Get("state") as string : "ready";
                    // была включена — включаем; была выключена, отсутствовала или не прочиталась — не трогаем
                    if (state != null && state != "disabled" && state != "missing" && TaskState(Tweak.Str(a, "path")) != "missing")
                        TaskSet(Tweak.Str(a, "path"), true);
                    break;
                case "command": Command(Tweak.Str(a, "revert")); break;
                case "power_scheme": PowerRestore(a, s); break;
                default: throw new NotSupportedException(Type(a));
            }
        }

        static string Type(D a) => Tweak.Str(a, "type");
        static object Get(this D d, string key) => d != null && d.TryGetValue(key, out var v) ? v : null;
        static RegistryHive Hive(D a) => Tweak.Str(a, "hive") == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;

        // ---------- реестр ----------

        static readonly Dictionary<RegistryValueKind, string> KindNames = new Dictionary<RegistryValueKind, string>
        {
            [RegistryValueKind.DWord] = "DWORD", [RegistryValueKind.QWord] = "QWORD", [RegistryValueKind.String] = "SZ",
            [RegistryValueKind.ExpandString] = "EXPAND_SZ", [RegistryValueKind.MultiString] = "MULTI_SZ",
            [RegistryValueKind.Binary] = "BINARY", [RegistryValueKind.None] = "NONE",
        };

        /// <summary>Значение как его видит Python-версия: DWORD/QWORD — беззнаковое число, MULTI_SZ — список строк.</summary>
        public static D Read(RegistryHive hive, string path, string name)
        {
            using (var k = Reg.Root(hive).OpenSubKey(path))
            {
                if (k == null || !k.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return new D { ["exists"] = false };
                var kind = k.GetValueKind(name);
                object v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (kind == RegistryValueKind.DWord) v = (long)unchecked((uint)(int)v);
                else if (kind == RegistryValueKind.QWord) v = (long)v;
                return new D { ["exists"] = true, ["value"] = v, ["kind"] = KindNames.TryGetValue(kind, out var n) ? (object)n : (int)kind };
            }
        }

        static RegistryValueKind KindOf(object kind)
        {
            if (kind is string s)
                return s switch
                {
                    "DWORD" => RegistryValueKind.DWord, "QWORD" => RegistryValueKind.QWord, "SZ" => RegistryValueKind.String,
                    "EXPAND_SZ" => RegistryValueKind.ExpandString, "MULTI_SZ" => RegistryValueKind.MultiString,
                    "BINARY" => RegistryValueKind.Binary, _ => RegistryValueKind.None,
                };
            return (RegistryValueKind)Convert.ToInt32(kind); // редкий тип, сохранённый в бэкапе числом
        }

        public static void Write(RegistryHive hive, string path, string name, object kind, object value)
        {
            var k = KindOf(kind);
            object v;
            switch (k)
            {
                case RegistryValueKind.DWord: v = unchecked((int)(uint)Convert.ToInt64(value)); break;
                case RegistryValueKind.QWord: v = Convert.ToInt64(value); break;
                case RegistryValueKind.MultiString: v = ((IEnumerable)value).Cast<object>().Select(x => x?.ToString() ?? "").ToArray(); break;
                case RegistryValueKind.Binary: v = value is string hex ? HexToBytes(hex) : ((IEnumerable)value).Cast<object>().Select(Convert.ToByte).ToArray(); break;
                case RegistryValueKind.None: v = new byte[0]; break;
                default: v = value?.ToString() ?? ""; break;
            }
            using (var key = Reg.Root(hive).CreateSubKey(path, true)) key.SetValue(name, v, k);
        }

        static byte[] HexToBytes(string hex) => Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();

        static void Delete(RegistryHive hive, string path, string name)
        {
            using (var k = Reg.Root(hive).OpenSubKey(path, true)) k?.DeleteValue(name, false);
        }

        static bool Same(object cur, object want)
        {
            if (cur == null || want == null) return cur == want;
            if (IsNumber(cur) && IsNumber(want)) return Convert.ToInt64(cur) == Convert.ToInt64(want);
            if (cur is string a && want is string b) return a == b;
            if (cur is IEnumerable ca && want is IEnumerable cb) return ca.Cast<object>().Select(x => x?.ToString()).SequenceEqual(cb.Cast<object>().Select(x => x?.ToString()));
            return cur.Equals(want);
        }

        static bool IsNumber(object o) => o is int || o is long || o is uint || o is decimal || o is double;

        static bool RegistryDetect(D a)
        {
            var cur = Read(Hive(a), Tweak.Str(a, "path"), Tweak.Str(a, "name"));
            return (bool)cur["exists"] && Same(cur["value"], a.Get("value"));
        }

        static D RegistrySnapshot(D a)
        {
            var snap = Read(Hive(a), Tweak.Str(a, "path"), Tweak.Str(a, "name"));
            if (snap.Get("value") is byte[] bytes) // REG_BINARY в JSON не ложится, храним hex
            {
                snap["value"] = string.Concat(bytes.Select(b => b.ToString("x2")));
                snap["hex"] = true;
            }
            return snap;
        }

        static void RegistryRestore(D a, D snap)
        {
            if (snap == null) // бэкапа нет: default из JSON (null — удалить значение)
                snap = new D { ["exists"] = a.Get("default") != null, ["value"] = a.Get("default"), ["kind"] = a["kind"] };
            if (snap.Get("exists") is bool e && e)
            {
                object value = snap.Get("hex") is bool h && h ? HexToBytes((string)snap["value"]) : snap["value"];
                Write(Hive(a), Tweak.Str(a, "path"), Tweak.Str(a, "name"), snap["kind"], value);
            }
            else Delete(Hive(a), Tweak.Str(a, "path"), Tweak.Str(a, "name"));
        }

        // ---------- раздел реестра (важно само его наличие) ----------

        static bool KeyExists(D a)
        {
            using (var k = Reg.Root(Hive(a)).OpenSubKey(Tweak.Str(a, "path"))) return k != null;
        }

        static bool RegKeyDetect(D a)
        {
            var cur = Read(Hive(a), Tweak.Str(a, "path"), "");
            return (bool)cur["exists"] && Same(cur["value"], Tweak.Str(a, "value") ?? "");
        }

        static void RegKeyRestore(D a, D snap)
        {
            if (snap != null && snap.Get("existed") is bool ex && ex)
            {
                var d = new D(a) { ["name"] = "", ["kind"] = "SZ" };
                RegistryRestore(d, snap.Get("default") as D);
                return;
            }
            string path = Tweak.Str(a, "path"), root = Tweak.Str(a, "root") ?? path;
            while (true) // удаляем раздел и пустых родителей вверх до root
            {
                try { Reg.Root(Hive(a)).DeleteSubKey(path, false); }
                catch (InvalidOperationException) { break; } // в разделе что-то есть: дальше не трогаем
                if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase) || !path.Contains("\\")) break;
                path = path.Substring(0, path.LastIndexOf('\\'));
                using (var k = Reg.Root(Hive(a)).OpenSubKey(path))
                    if (k != null && (k.SubKeyCount > 0 || k.ValueCount > 0)) break;
            }
        }

        // ---------- службы ----------

        const string SvcKey = @"SYSTEM\CurrentControlSet\Services\";

        static string ServiceMode(string name)
        {
            var start = Read(RegistryHive.LocalMachine, SvcKey + name, "Start");
            if (!(start.Get("exists") is bool e && e)) return null;
            string mode = Convert.ToInt64(start["value"]) switch { 2 => "auto", 3 => "demand", 4 => "disabled", _ => null };
            if (mode == "auto" && Read(RegistryHive.LocalMachine, SvcKey + name, "DelayedAutostart").Get("value") is long d && d == 1)
                mode = "delayed-auto";
            return mode;
        }

        static void ServiceSet(string name, string mode)
        {
            var (code, output) = SysInfo.Run("sc.exe", $"config \"{name}\" start= {mode}");
            if (code != 0 && mode == "delayed-auto") // не всем службам Windows разрешает отложенный запуск (SysMain: ошибка 87)
                (code, output) = SysInfo.Run("sc.exe", $"config \"{name}\" start= auto");
            if (code != 0) throw new InvalidOperationException($"sc config {name}: {output.Trim()}");
        }

        static bool ServiceDetect(D a)
        {
            var mode = ServiceMode(Tweak.Str(a, "name"));
            return mode == null ? Tweak.Str(a, "start") == "disabled" : mode == Tweak.Str(a, "start"); // службы нет — отключать нечего
        }

        static void ServiceApply(D a)
        {
            string name = Tweak.Str(a, "name"), start = Tweak.Str(a, "start");
            if (ServiceMode(name) == null)
            {
                if (start == "disabled") return;
                throw new InvalidOperationException($"служба {name} не найдена");
            }
            ServiceSet(name, start);
            if (start == "disabled") SysInfo.Run("sc.exe", $"stop \"{name}\""); // уже остановлена — не важно
        }

        // ---------- задачи планировщика ----------

        internal static readonly string TasksDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
        internal static readonly XNamespace TaskNs = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        /// <summary>'ready' / 'disabled' / 'missing' или null, если прочитать не удалось.</summary>
        internal static string TaskState(string path)
        {
            try
            {
                var doc = XDocument.Load(Path.Combine(TasksDir, path.TrimStart('\\')));
                var enabled = doc.Root?.Element(TaskNs + "Settings")?.Element(TaskNs + "Enabled");
                return enabled != null && enabled.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) ? "disabled" : "ready";
            }
            catch (FileNotFoundException) { return "missing"; }
            catch (DirectoryNotFoundException) { return "missing"; }
            catch (Exception) { } // нет прав на файл — спрашиваем планировщик
            int cut = path.LastIndexOf('\\');
            string folder = path.Substring(0, cut), name = path.Substring(cut + 1);
            var ps = $"try {{ (Get-ScheduledTask -TaskPath '{folder}\\' -TaskName '{name}' -ErrorAction Stop).State }} " +
                     "catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { 'missing' } else { throw } }";
            var (code, output) = SysInfo.Run("powershell", $"-NoProfile -Command \"{ps.Replace("\"", "\\\"")}\"");
            return code == 0 ? output.Trim().ToLowerInvariant() : null;
        }

        internal static void TaskSet(string path, bool enable)
        {
            var (code, output) = SysInfo.Run("schtasks.exe", $"/Change /TN \"{path}\" {(enable ? "/Enable" : "/Disable")}");
            if (code != 0) throw new InvalidOperationException($"schtasks {path}: {output.Trim()}");
        }

        static bool TaskDetect(D a)
        {
            var s = TaskState(Tweak.Str(a, "path"));
            return s == "disabled" || s == "missing";
        }

        // ---------- произвольная команда ----------

        static void Command(string cmd)
        {
            var (code, output) = SysInfo.Run("cmd.exe", "/c " + cmd);
            if (code != 0) throw new InvalidOperationException($"{cmd}: {(output.Trim().Length > 0 ? output.Trim() : "код " + code)}");
        }

        static bool CommandDetect(D a)
        {
            var (_, output) = SysInfo.Run("cmd.exe", "/c " + Tweak.Str(a, "detect"));
            return output.IndexOf(Tweak.Str(a, "detect_contains") ?? "\0", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---------- схема питания ----------

        static readonly Regex Guid = new Regex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase);
        const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

        static string ActiveScheme()
        {
            var (code, output) = SysInfo.Run("powercfg", "/getactivescheme");
            var m = code == 0 ? Guid.Match(output) : null;
            return m != null && m.Success ? m.Value.ToLowerInvariant() : null;
        }

        static void SetScheme(string guid)
        {
            var (code, output) = SysInfo.Run("powercfg", "/setactive " + guid);
            if (code != 0) throw new InvalidOperationException($"powercfg /setactive {guid}: {output.Trim()}");
        }

        static HashSet<string> Schemes()
        {
            var (code, output) = SysInfo.Run("powercfg", "/list");
            if (code != 0) throw new InvalidOperationException("powercfg /list: " + output.Trim());
            return new HashSet<string>(Guid.Matches(output).Cast<Match>().Select(m => m.Value.ToLowerInvariant()));
        }

        /// <summary>«Включено» — активна своя копия схемы или сам шаблон source (если он открыт в системе).</summary>
        static HashSet<string> Targets(D a)
        {
            var t = new HashSet<string> { Tweak.Str(a, "scheme").ToLowerInvariant() };
            if (Tweak.Str(a, "source") is string src) t.Add(src.ToLowerInvariant());
            return t;
        }

        static bool PowerDetect(D a) => ActiveScheme() is string s && Targets(a).Contains(s);

        static void PowerApply(D a)
        {
            var schemes = Schemes();
            string guid = Tweak.Str(a, "scheme").ToLowerInvariant(), source = (Tweak.Str(a, "source") ?? "").ToLowerInvariant();
            if (!schemes.Contains(guid))
            {
                if (schemes.Contains(source)) guid = source;
                else if (source.Length > 0)
                {
                    var (code, output) = SysInfo.Run("powercfg", $"-duplicatescheme {source} {guid}");
                    if (code != 0) throw new InvalidOperationException("не удалось создать схему питания: " + output.Trim());
                }
                else throw new InvalidOperationException("этой схемы питания нет на ПК (часто так на ноутбуках с Modern Standby)");
            }
            SetScheme(guid);
        }

        static void PowerRestore(D a, D snap)
        {
            string fallback = Tweak.Str(a, "default") ?? Balanced;
            string guid = snap.Get("scheme") as string ?? fallback;
            if (Targets(a).Contains(guid)) guid = fallback; // до включения и так была эта схема
            try { SetScheme(guid); }
            catch (InvalidOperationException) { if (guid == fallback) throw; SetScheme(fallback); }
            if (Tweak.Str(a, "source") != null && Schemes().Contains(Tweak.Str(a, "scheme").ToLowerInvariant()))
                SysInfo.Run("powercfg", "-delete " + Tweak.Str(a, "scheme")); // не удалилась — схема просто останется
        }
    }
}
