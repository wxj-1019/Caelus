// @author zenjiro 18967498922@163.com
// 文件用途 维护动作·启动项审查：新发现报告 + 只禁不删（备份可还原）

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CaelusApp
{
    internal sealed class StartupAuditAction : IHealthAction, IHealthAutoCycle
    {
        public string Id { get { return "startup-audit"; } }
        public string TitleKey { get { return "health.startup.title"; } }
        public string DescKey { get { return "health.startup.desc"; } }
        public bool AllowAuto { get { return false; } }   // 自动周期只扫描报告
        public bool CanUndo { get { return true; } }

        // —— 测试挂钩（生产全为 null 走真实注册表/文件）——
        internal static Func<List<StartupAudit.Entry>> ScanCurrentHook;
        internal static Func<string, string, string> ReadRunValueHook;          // (hive,name)→data|null
        internal static Func<string, string, string, string> WriteRunValueHook; // (hive,name,data)→error|null（同名已存在须报错）
        internal static Func<string, string, string> DeleteRunValueHook;        // →error|null
        internal static Func<string, string, string, string> BackupWriteHook;   // 备份允许覆盖
        internal static Func<string, string, string> BackupDeleteHook;
        internal static Func<string, List<KeyValuePair<string, string>>> BackupEnumHook;
        internal static string StartupFolderOverride;
        internal static string BackupDirOverride;
        internal static string BaselinePathOverride;

        private const string BackupKeyPath = @"SOFTWARE\Caelus\DisabledStartup";

        public HealthReport Analyze()
        {
            var current = Scan();
            var baseline = StartupAudit.LoadBaseline(BaselinePath());
            var added = StartupAudit.DiffNew(current, baseline);
            var report = new HealthReport { ActionId = Id };
            foreach (StartupAudit.Entry e in added)
                report.Findings.Add(new HealthFinding
                {
                    Id = e.Source + "|" + e.Name,
                    Label = e.Name,
                    Detail = e.Source == "StartupFolder" ? "启动文件夹" : e.Command,
                    Risky = IsSystemItem(e)
                });
            return report;
        }

        /// <summary>自动周期职责：新发现写新闻（沿用旧 HealthCare 行为）+ 提交基线。</summary>
        public void OnAutoCycle()
        {
            var current = Scan();
            string bp = BaselinePath();
            var baseline = StartupAudit.LoadBaseline(bp);
            var added = StartupAudit.DiffNew(current, baseline);
            if (baseline.Count > 0 && added.Count > 0)
            {
                var names = new List<string>();
                foreach (StartupAudit.Entry e in added) names.Add(e.Name + "（" + e.Source + "）");
                string news = string.Join("、", names.ToArray());
                if (news.Length > 300) news = news.Substring(0, 300) + "...";
                Settings.SaveStr("HealthStartupNews", news);
                Logger.Log("健康维护：发现 " + added.Count + " 个新启动项：" + news);
            }
            StartupAudit.SaveBaseline(bp, current);
        }

        public HealthResult Execute(string[] selectedIds)
        {
            if (selectedIds == null || selectedIds.Length == 0)
                return new HealthResult { ActionId = Id, Outcome = HealthOutcome.Skipped, Summary = "未勾选启动项（自动维护从不禁用启动项）" };
            var want = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
            int done = 0;
            var payloadLines = new List<string>();
            var names = new List<string>();
            foreach (StartupAudit.Entry e in Scan())
            {
                string id = e.Source + "|" + e.Name;
                if (!want.Contains(id)) continue;
                if (IsSystemItem(e)) continue;   // 二次校验：UI 被绕过也禁不了系统项
                string payload;
                string err;
                if (!DisableOne(e, out payload, out err))
                    return new HealthResult { ActionId = Id, Outcome = HealthOutcome.Failed, Error = "禁用 " + e.Name + " 失败：" + err };
                done++;
                payloadLines.Add(payload);
                names.Add(e.Name);
            }
            if (done == 0)
                return new HealthResult { ActionId = Id, Outcome = HealthOutcome.Skipped, Summary = "勾选项均已不存在或受系统保护" };
            return new HealthResult
            {
                ActionId = Id, Outcome = HealthOutcome.Success, ItemCount = done,
                Summary = "已禁用 " + done + " 个启动项：" + string.Join("、", names.ToArray()),
                UndoPayload = string.Join("\n", payloadLines.ToArray())
            };
        }

        public List<HealthFinding> ListDisabled()
        {
            var list = new List<HealthFinding>();
            foreach (string hive in new[] { "HKCU\\Run", "HKLM\\Run" })
            {
                foreach (KeyValuePair<string, string> kv in BackupEnum(hive))
                {
                    list.Add(new HealthFinding
                    {
                        Id = HealthEsc.Esc(hive) + "\t" + HealthEsc.Esc(kv.Key) + "\t" + HealthEsc.Esc(kv.Value) + "\t",
                        Label = kv.Key, Detail = kv.Value
                    });
                }
            }
            try
            {
                string dir = BackupDir();
                if (Directory.Exists(dir))
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        string orig = Path.Combine(StartupFolder(), Path.GetFileName(f));
                        list.Add(new HealthFinding
                        {
                            Id = HealthEsc.Esc("StartupFolder") + "\t" + HealthEsc.Esc(Path.GetFileName(f)) + "\t\t" + HealthEsc.Esc(orig),
                            Label = Path.GetFileName(f), Detail = "启动文件夹"
                        });
                    }
            }
            catch { }
            return list;
        }

        public bool Undo(string undoPayload, out string error)
        {
            error = null;
            string[] f = (undoPayload ?? "").Split('\t');
            if (f.Length < 4) { error = "还原负载损坏"; return false; }
            string source = HealthEsc.Unesc(f[0]);
            string name = HealthEsc.Unesc(f[1]);
            string data = HealthEsc.Unesc(f[2]);
            string extra = HealthEsc.Unesc(f[3]);
            if (source != "HKCU\\Run" && source != "HKLM\\Run" && source != "StartupFolder")
            { error = "未知的负载来源：" + source; return false; }
            if (source == "StartupFolder")
            {
                if (name != Path.GetFileName(name)) { error = "负载中的文件名不合法"; return false; }   // 防 ".." 逸出备份目录
                string bak = Path.Combine(BackupDir(), name);
                if (!File.Exists(bak)) { error = "备份文件已不存在"; return false; }
                if (File.Exists(extra)) { error = "启动文件夹已存在同名文件"; return false; }
                try
                {
                    Directory.CreateDirectory(StartupFolder());
                    File.Move(bak, extra);
                    return true;
                }
                catch (Exception ex) { error = ex.GetType().Name; return false; }
            }
            // 注册表还原：目标已有同名值不覆盖
            if (ReadRunValue(source, name) != null) { error = "目标位置已有同名值，未覆盖"; return false; }
            string werr = WriteRunValue(source, name, data);
            if (werr != null) { error = werr; return false; }
            BackupDelete(source, name);
            return true;
        }

        /// <summary>系统/微软项判定（纯逻辑可单测）：命令路径在 Windows 目录或含 Microsoft 目录段。</summary>
        internal static bool IsSystemItem(StartupAudit.Entry e)
        {
            string c = e != null ? e.Command ?? "" : "";
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (win.Length > 0 && c.IndexOf(win, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return c.IndexOf("\\Microsoft\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // —— 以下为效果层：真实注册表/文件操作，测试经挂钩替换 ——

        private static bool DisableOne(StartupAudit.Entry e, out string payload, out string error)
        {
            payload = null; error = null;
            if (e.Source == "StartupFolder")
            {
                string src = Path.Combine(StartupFolder(), e.Name);
                string dst = Path.Combine(BackupDir(), e.Name);
                if (!File.Exists(src)) { error = "文件已不存在"; return false; }
                try
                {
                    Directory.CreateDirectory(BackupDir());
                    // 同名陈旧备份允许覆盖，当前活动文件始终保全
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                }
                catch (Exception ex) { error = ex.GetType().Name; return false; }
                payload = HealthEsc.Esc(e.Source) + "\t" + HealthEsc.Esc(e.Name) + "\t\t" + HealthEsc.Esc(src);
                return true;
            }
            string data = ReadRunValue(e.Source, e.Name);
            if (data == null) { error = "值已不存在"; return false; }
            error = BackupWrite(e.Source, e.Name, data);
            if (error != null) return false;
            error = DeleteRunValue(e.Source, e.Name);
            if (error != null) { BackupDelete(e.Source, e.Name); return false; }   // 删失败回滚备份
            payload = HealthEsc.Esc(e.Source) + "\t" + HealthEsc.Esc(e.Name) + "\t" + HealthEsc.Esc(data) + "\t";
            return true;
        }

        private static List<StartupAudit.Entry> Scan()
        {
            if (ScanCurrentHook != null) return ScanCurrentHook();
            return StartupAudit.ScanCurrent();
        }

        private static string BaselinePath() { return BaselinePathOverride ?? StartupAudit.BaselinePath; }
        private static string StartupFolder()
        {
            return StartupFolderOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        }
        private static string BackupDir()
        {
            return BackupDirOverride ?? Path.Combine(Paths.Data, "DisabledStartup");
        }

        private static string ReadRunValue(string hive, string name)
        {
            if (ReadRunValueHook != null) return ReadRunValueHook(hive, name);
            try
            {
                using (RegistryKey k = OpenRunKey(hive, false))
                {
                    if (k == null) return null;
                    object v = k.GetValue(name, null);
                    return v == null ? null : Convert.ToString(v);
                }
            }
            catch { return null; }
        }

        private static string WriteRunValue(string hive, string name, string data)
        {
            if (WriteRunValueHook != null) return WriteRunValueHook(hive, name, data);
            try
            {
                using (RegistryKey k = OpenRunKey(hive, true))
                {
                    if (k == null) return "无法打开 Run 键";
                    if (k.GetValue(name, null) != null) return "目标位置已有同名值";
                    k.SetValue(name, data);
                    return null;
                }
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        private static string DeleteRunValue(string hive, string name)
        {
            if (DeleteRunValueHook != null) return DeleteRunValueHook(hive, name);
            try
            {
                using (RegistryKey k = OpenRunKey(hive, true))
                {
                    if (k == null) return "无法打开 Run 键";
                    k.DeleteValue(name, false);
                    return null;
                }
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        private static RegistryKey OpenRunKey(string hive, bool writable)
        {
            const string runPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            if (hive == "HKLM\\Run") return Registry.LocalMachine.OpenSubKey(runPath, writable);
            return Registry.CurrentUser.OpenSubKey(runPath, writable);
        }

        private static string BackupWrite(string hive, string name, string data)
        {
            if (BackupWriteHook != null) return BackupWriteHook(hive, name, data);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(BackupKeyPath + "\\" + HiveTag(hive)))
                {
                    if (k == null) return "无法创建备份键";
                    k.SetValue(name, data);
                    return null;
                }
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        private static string BackupDelete(string hive, string name)
        {
            if (BackupDeleteHook != null) return BackupDeleteHook(hive, name);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(BackupKeyPath + "\\" + HiveTag(hive), true))
                {
                    if (k != null) k.DeleteValue(name, false);
                    return null;
                }
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        private static List<KeyValuePair<string, string>> BackupEnum(string hive)
        {
            if (BackupEnumHook != null) return BackupEnumHook(hive);
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(BackupKeyPath + "\\" + HiveTag(hive)))
                {
                    if (k == null) return list;
                    foreach (string n in k.GetValueNames())
                        list.Add(new KeyValuePair<string, string>(n, Convert.ToString(k.GetValue(n, ""))));
                }
            }
            catch { }
            return list;
        }

        private static string HiveTag(string hive) { return hive == "HKLM\\Run" ? "HKLM" : "HKCU"; }
    }
}
