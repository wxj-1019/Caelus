// @author zenjiro 18967498922@163.com
// 文件用途 DevFocus 场景的自测：仲裁集成、活性报告、开关语义、抢占挂起

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static ProcessChange MakeChange(int pid, string name, ProcessChangeKind kind)
        {
            var pc = new ProcessChange();
            pc.Pid = pid;
            pc.Name = name;
            pc.Kind = kind;
            return pc;
        }

        /// <summary>把当前自测 exe 复制为指定文件名并启动心跳探针——
        /// 得到一个"进程名匹配 BuildCatalog"的真实活进程。</summary>
        private static Process StartNamedProbe(string dir, string exeName, out string beat)
        {
            beat = Path.Combine(dir, exeName + ".beat");
            string copy = Path.Combine(dir, exeName);
            File.Copy(Application.ExecutablePath, copy, true);
            var psi = new ProcessStartInfo(copy, "--test-heartbeat-probe " + Quote(beat));
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            return Process.Start(psi);
        }

        /// <summary>Game 场景占位：GameMode 尚未实现 IScenario，
        /// 注册后仲裁器才会让 Game（优先级 100）抢占 DevFocus（50）。</summary>
        private sealed class StubGameScenario : IScenario
        {
            public ScenarioKind Kind { get { return ScenarioKind.Game; } }
            public int Priority { get { return 100; } }
            public void Grant() { }
            public void Suspend() { }
        }

        // 注意：Grant 会真实调 SvcPause.Activate（暂停 SysMain/WSearch）——
        // 每个测试的 finally 必须 dev.Stop() 兜底还原，断言失败也不能把服务留在暂停态
        private static void TestDevFocusGrantAndRelease()
        {
            string dir = NewTempDir("devfocus-grant");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                string beat;
                probe = StartNamedProbe(dir, "msbuild.exe", out beat);
                WaitAdvance(beat, -1, 4000);

                // 编译进程启动 → DevFocus 报告活跃 → 仲裁器授权
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsActive);
                Eq(true, dev.IsGranted);
                Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);

                // 编译进程退出 → 报告不活跃 → 仲裁器收回授权
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Stopped) }, false));
                Eq(false, dev.IsActive);
                Eq(false, dev.IsGranted);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDevFocusPreemptedByGame()
        {
            string dir = NewTempDir("devfocus-preempt");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                arbiter.Register(new StubGameScenario());
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                string beat;
                probe = StartNamedProbe(dir, "csc.exe", out beat);
                WaitAdvance(beat, -1, 4000);
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "csc", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsGranted);

                // 游戏激活 → DevFocus 被挂起（副作用还原），但活性检测保留
                arbiter.ReportActivity(ScenarioKind.Game, true);
                Eq(false, dev.IsGranted);
                Eq(true, dev.IsActive);
                Eq<ScenarioKind?>(ScenarioKind.Game, arbiter.CurrentGranted);

                // 游戏退出 → DevFocus 补位恢复
                arbiter.ReportActivity(ScenarioKind.Game, false);
                Eq(true, dev.IsGranted);
                Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDevFocusDisabledSwitch()
        {
            string dir = NewTempDir("devfocus-off");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                bool on = false;
                dev = new DevFocus(arbiter, core, () => on, (n, p) => false, name => false);

                string beat;
                probe = StartNamedProbe(dir, "msbuild.exe", out beat);
                WaitAdvance(beat, -1, 4000);
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                // 开关关闭：不报告、不掌权
                Eq(false, dev.IsActive);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);

                // 开关打开后事件到达：正常激活；再关闭：立即解除
                on = true;
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsGranted);
                on = false;
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new ProcessChange[0], false));
                Eq(false, dev.IsGranted);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDevFocusSuppressionDecision()
        {
            string winRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            int self = Process.GetCurrentProcess().Id;
            int session = Process.GetCurrentProcess().SessionId;
            var noWindows = new HashSet<int>();
            Func<string, string, bool> noWhitelist = (n, p) => false;

            // 普通用户后台进程：应压制
            Eq(true, DevFocus.ShouldSuppressBackground(
                5000, self, "someapp", @"C:\Apps\someapp.exe",
                session, session, 0, noWindows, winRoot, noWhitelist));

            // 前台程序：豁免
            Eq(false, DevFocus.ShouldSuppressBackground(
                5000, self, "someapp", @"C:\Apps\someapp.exe",
                session, session, 5000, noWindows, winRoot, noWhitelist));

            // 有可见窗口的程序：豁免（常规档不动带窗口程序）
            var visible = new HashSet<int>(); visible.Add(5000);
            Eq(false, DevFocus.ShouldSuppressBackground(
                5000, self, "someapp", @"C:\Apps\someapp.exe",
                session, session, 0, visible, winRoot, noWhitelist));

            // 反作弊进程：豁免（任何强度不动摇）
            Eq(false, DevFocus.ShouldSuppressBackground(
                5001, self, "vgc", @"C:\Riot\vgc.exe",
                session, session, 0, noWindows, winRoot, noWhitelist));

            // 别的登录账户的进程：豁免
            Eq(false, DevFocus.ShouldSuppressBackground(
                5002, self, "someapp", @"C:\Apps\someapp.exe",
                session + 1, session, 0, noWindows, winRoot, noWhitelist));

            // 白名单命中：豁免
            Eq(false, DevFocus.ShouldSuppressBackground(
                5003, self, "mytool", @"C:\Tools\mytool.exe",
                session, session, 0, noWindows, winRoot, (n, p) => true));
        }

        private static void TestDevFocusBuildReasonIsolation()
        {
            string dir = NewTempDir("devfocus-buildbit");
            Process probe = null;
            try
            {
                string beat = Path.Combine(dir, "p.beat");
                probe = StartNamedProbe(dir, "testhelper.exe", out beat);
                WaitAdvance(beat, -1, 4000);

                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                try
                {
                    // 同一进程先被游戏位压制、再被编译位压制（引用计数语义）
                    core.Acquire(probe.Id, probe.ProcessName,
                        SuppressReason.Background, null, SuppressionLevel.Eco);
                    core.Acquire(probe.Id, probe.ProcessName,
                        SuppressReason.Build, "devfocus", SuppressionLevel.Eco);
                    Eq(true, core.HasReason(probe.Id, SuppressReason.Background));
                    Eq(true, core.HasReason(probe.Id, SuppressReason.Build));

                    // 按编译位还原：游戏位仍在，进程仍被压制
                    core.ReleaseReason(SuppressReason.Build);
                    Eq(false, core.HasReason(probe.Id, SuppressReason.Build));
                    Eq(true, core.HasReason(probe.Id, SuppressReason.Background));
                    Eq(true, core.IsThrottled(probe.Id));

                    // 按游戏位还原后彻底解除
                    core.ReleaseReason(SuppressReason.Background);
                    Eq(false, core.IsThrottled(probe.Id));
                }
                finally { core.ReleaseReason(SuppressReason.Background | SuppressReason.Build); }
            }
            finally
            {
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDevFocusActivitySources()
        {
            string dir = NewTempDir("devfocus-sources");
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                // 初始：无任何活性来源
                Eq(false, dev.IsActive);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);

                // 专注开关
                dev.SetFocusMode(true);
                Eq(true, dev.IsActive);
                Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);
                dev.SetFocusMode(false);
                Eq(false, dev.IsActive);
                Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                try { Settings.Save("DevFocusModeOn", false); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDevFocusFocusGrantEffects()
        {
            string dir = NewTempDir("devfocus-fx");
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                arbiter.Register(new StubGameScenario());
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                // 专注开 → 掌权 → 校正定时器启动
                dev.SetFocusMode(true);
                Eq(true, dev.IsGranted);
                Eq(true, dev.FocusTimerRunning);

                // 游戏抢占 → 挂起 → 定时器必须停止（挂起场景零后台开销）
                arbiter.ReportActivity(ScenarioKind.Game, true);
                Eq(false, dev.IsGranted);
                Eq(false, dev.FocusTimerRunning);
                // 活性仍在（专注开关还开着）
                Eq(true, dev.IsActive);

                // 游戏退出 → 补位 → 定时器恢复
                arbiter.ReportActivity(ScenarioKind.Game, false);
                Eq(true, dev.IsGranted);
                Eq(true, dev.FocusTimerRunning);

                // 专注关 → 整体解除
                dev.SetFocusMode(false);
                Eq(false, dev.IsGranted);
                Eq(false, dev.FocusTimerRunning);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                try { Settings.Save("DevFocusModeOn", false); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestDevFocusDistractOnce()
        {
            string dir = NewTempDir("devfocus-distract");
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false,
                    name => string.Equals(name, "discord", StringComparison.OrdinalIgnoreCase));

                var balloons = new List<string>();
                dev.SessionChanged += key => balloons.Add(key);

                dev.SetFocusMode(true);
                Eq(true, dev.IsGranted);

                // 同名分心进程两次启动：气球只报一次
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(42001, "discord", ProcessChangeKind.Started) }, false));
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(42002, "discord", ProcessChangeKind.Started) }, false));
                int distractCount = 0;
                foreach (string k in balloons) if (k == "bal.distract") distractCount++;
                Eq(1, distractCount);

                // 专注关闭后再开：清空已报集合，可再次提醒
                dev.SetFocusMode(false);
                dev.SetFocusMode(true);
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(42003, "discord", ProcessChangeKind.Started) }, false));
                distractCount = 0;
                foreach (string k in balloons) if (k == "bal.distract") distractCount++;
                Eq(2, distractCount);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                try { Settings.Save("DevFocusModeOn", false); } catch { }
                DeleteTempDir(dir);
            }
        }
        private static void TestIdeCatalogMatch()
        {
            Eq(true, IdeCatalog.IsMatch("devenv",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe"));
            Eq(false, IdeCatalog.IsMatch("code", @"C:\Temp\code.exe"));
            Eq(false, IdeCatalog.IsMatch("notepad",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\notepad.exe"));
            Eq(false, IdeCatalog.IsMatch(null, null));
            Eq(false, IdeCatalog.IsMatch("devenv", null));
        }

        private static void TestDevFocusIdeBoostRestore()
        {
            string dir = NewTempDir("devfocus-ide");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                string beat = Path.Combine(dir, "ide.beat");
                probe = StartProbe(beat);
                WaitAdvance(beat, -1, 4000);
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);

                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                // 测试钩子绕过窗口条件直接提优：AboveNormal 生效
                Eq(true, dev.BoostIdeForTest(probe.Id));
                probe.Refresh();
                Eq(ProcessPriorityClass.AboveNormal, probe.PriorityClass);

                // 重复提优幂等（快照不叠加）
                Eq(true, dev.BoostIdeForTest(probe.Id));
                probe.Refresh();
                Eq(ProcessPriorityClass.AboveNormal, probe.PriorityClass);

                // 还原：回到 Normal
                dev.RestoreIdeBoost();
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);
            }
            finally
            {
                if (dev != null) try { dev.RestoreIdeBoost(); } catch { }
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }
    }
}
