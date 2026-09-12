// @author zenjiro 18967498922@163.com
// 文件用途 DevFocus 场景的自测：仲裁集成、活性报告、开关语义、抢占挂起

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
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

                // —— 阻断开关打开：首次命中 = 提醒+阻断（假 PID 关闭失败被隔离），
                //     再次命中 = 仍阻断但气球被 30 秒/名限频压住 ——
                Settings.Save("DevFocusDistractBlock", true);
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(42004, "discord", ProcessChangeKind.Started) }, false));
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(42005, "discord", ProcessChangeKind.Started) }, false));
                int blockCount = 0;
                foreach (string k in balloons) if (k == "bal.distract.block") blockCount++;
                Eq(1, blockCount);
                int distractInBlock = 0;
                foreach (string k in balloons) if (k == "bal.distract") distractInBlock++;
                Eq(2, distractInBlock);   // 已提醒过的 discord 不再发普通提醒
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                try { Settings.Save("DevFocusModeOn", false); } catch { }
                try { Settings.Save("DevFocusDistractBlock", false); } catch { }
                try { FocusStats.ResetForTest(); } catch { }
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

        private static int QueryIoOf(int pid)
        {
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return -2;
            try { return Native.QueryIoPriority(h); }
            finally { Native.CloseHandle(h); }
        }

        private static void TestDevFocusIdeBoostRestore()
        {

            if (LolAceClientRunning())
                Skip("检测到 LOL/ACE 客户端家族运行，其驱动干扰非游戏进程优先级操作");
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
                int ioBefore = QueryIoOf(probe.Id);
                if (ioBefore < 2)
                    // 环境敏感：父进程链被压制后 IO 优先级被子进程继承（如强退的
                    // 宿主残留），此时往返仍验证「提升到 3 → 还原到原值」，跳过门槛
                    Skip("探针 IO 优先级被环境压制（继承自父进程链），基线低于常态");

                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                // 测试钩子绕过窗口条件直接提优：AboveNormal + IO 3 生效
                Eq(true, dev.BoostIdeForTest(probe.Id));
                probe.Refresh();
                Eq(ProcessPriorityClass.AboveNormal, probe.PriorityClass);
                Eq(3, QueryIoOf(probe.Id));

                // 重复提优幂等（快照不叠加）
                Eq(true, dev.BoostIdeForTest(probe.Id));
                probe.Refresh();
                Eq(ProcessPriorityClass.AboveNormal, probe.PriorityClass);
                Eq(3, QueryIoOf(probe.Id));

                // 还原：优先级回到 Normal，IO 回到快照原值（不是写死的 2）
                dev.RestoreIdeBoost();
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);
                Eq(ioBefore, QueryIoOf(probe.Id));
            }
            finally
            {
                if (dev != null) try { dev.RestoreIdeBoost(); } catch { }
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }

        /// <summary>编译提优必须像 IDE 提优一样随快照还原：编译进程被提为 HIGH + IO 3，
        /// 场景被游戏抢占挂起后应回到原优先级与原 IO（此前的缺陷是只提不还）。</summary>
        private static void TestDevFocusBuildBoostSnapshotRestore()
        {
            string dir = NewTempDir("devfocus-buildboost");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                string beat;
                probe = StartNamedProbe(dir, "msbuild.exe", out beat);
                WaitAdvance(beat, -1, 4000);
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);
                int ioBefore = QueryIoOf(probe.Id);

                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                arbiter.Register(new StubGameScenario());
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                // 编译进程启动 → 掌权 → 提优 HIGH + IO 3
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsGranted);
                probe.Refresh();
                Eq(ProcessPriorityClass.High, probe.PriorityClass);
                Eq(3, QueryIoOf(probe.Id));

                // 游戏抢占挂起 → 编译提优按快照还原（优先级与 IO 都回到原值）
                arbiter.ReportActivity(ScenarioKind.Game, true);
                Eq(false, dev.IsGranted);
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);
                Eq(ioBefore, QueryIoOf(probe.Id));
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                DeleteTempDir(dir);
            }
        }

        /// <summary>掌权期间（如专注模式已开）才启动的编译进程也必须同步拿到提优——
        /// 提优不能只发生在 Grant 那一刻。</summary>
        private static void TestDevFocusIncrementalBuildBoost()
        {
            string dir = NewTempDir("devfocus-buildinc");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                // 先经专注开关掌权（此刻没有任何编译进程）
                dev.SetFocusMode(true);
                Eq(true, dev.IsGranted);

                // 掌权后编译进程才启动 → Started 事件应同步提优
                string beat;
                probe = StartNamedProbe(dir, "msbuild.exe", out beat);
                WaitAdvance(beat, -1, 4000);
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsGranted);
                probe.Refresh();
                Eq(ProcessPriorityClass.High, probe.PriorityClass);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                try { Settings.Save("DevFocusModeOn", false); } catch { }
                DeleteTempDir(dir);
            }
        }

        /// <summary>游戏接管共享效果（服务暂停 + 通知静默）时所有权直通：挂起不移交
        /// 底层还原，效果保持、占用方换成 game；无游戏接管时照常还原。
        /// 服务被禁用/不可停的机器上无法形成持久化标志，按约定记 SKIP。</summary>
        private static void TestDevFocusSharedEffectHandoff()
        {
            if (SvcState.Query("SysMain") != 4 && SvcState.Query("WSearch") != 4)
                Skip("SysMain/WSearch 均未运行，无法验证服务暂停持久化标志");

            string dir = NewTempDir("devfocus-handoff");
            Process probe = null;
            DevFocus dev = null;
            try
            {
                string beat;
                probe = StartNamedProbe(dir, "msbuild.exe", out beat);
                WaitAdvance(beat, -1, 4000);

                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                arbiter.Register(new StubGameScenario());
                bool gameSvc = false;
                bool gameQuiet = false;
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false,
                    () => gameSvc, () => gameQuiet);

                // —— A 段：服务暂停直通（build 掌权，专注关）——
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsGranted);
                Eq(true, SvcPause.HeldBy("devfocus"));
                if (!SvcPause.PersistFlagPresentForTest())
                    Skip("服务可查但无法形成持久化标志（权限或服务类型限制），无法验证直通");

                gameSvc = true;
                arbiter.ReportActivity(ScenarioKind.Game, true);
                Eq(false, dev.IsGranted);
                Eq(false, SvcPause.HeldBy("devfocus"));
                Eq(true, SvcPause.HeldBy("game"));
                Eq(true, SvcPause.PersistFlagPresentForTest());

                // 生产时序：游戏侧先按占用方释放，再让出仲裁席位
                SvcPause.Restore(SvcPause.OwnerGame);
                arbiter.ReportActivity(ScenarioKind.Game, false);
                Eq(true, dev.IsGranted);
                Eq(true, SvcPause.HeldBy("devfocus"));
                Eq(false, SvcPause.HeldBy("game"));

                // —— B 段：通知静默直通（专注模式打开，借下一次补位 grant 施加静默）——
                dev.SetFocusMode(true);
                gameSvc = false;
                arbiter.ReportActivity(ScenarioKind.Game, true);   // 游戏不要服务：svc 普通还原
                Eq(false, SvcPause.HeldBy("devfocus"));
                Eq(false, SvcPause.PersistFlagPresentForTest());
                Notif.Restore(Notif.OwnerGame);
                arbiter.ReportActivity(ScenarioKind.Game, false);  // 补位 grant：build+focus 都在 → 静默施加
                Eq(true, dev.IsGranted);
                Eq(true, SvcPause.HeldBy("devfocus"));
                Eq(true, Notif.HeldBy("devfocus"));

                gameQuiet = true;
                arbiter.ReportActivity(ScenarioKind.Game, true);   // 游戏要静默 → 通知走直通；svc 普通还原
                Eq(false, dev.IsGranted);
                Eq(true, Notif.HeldBy("game"));
                Eq(false, Notif.HeldBy("devfocus"));
                Eq(false, SvcPause.HeldBy("devfocus"));

                Notif.Restore(Notif.OwnerGame);
                arbiter.ReportActivity(ScenarioKind.Game, false);  // 补位 → 静默回到 devfocus
                Eq(true, dev.IsGranted);
                Eq(true, Notif.HeldBy("devfocus"));
                Eq(false, Notif.HeldBy("game"));

                // —— C 段：游戏两者都不要 → 全部按普通路径还原 ——
                gameQuiet = false;
                arbiter.ReportActivity(ScenarioKind.Game, true);
                Eq(false, dev.IsGranted);
                Eq(false, SvcPause.HeldBy("devfocus"));
                Eq(false, SvcPause.HeldBy("game"));
                Eq(false, Notif.HeldBy("devfocus"));
                Eq(false, Notif.HeldBy("game"));
                Eq(false, SvcPause.PersistFlagPresentForTest());
            }
            finally
            {
                try { SvcPause.Restore(); } catch { }
                try { Notif.Restore(); } catch { }
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                try { Settings.Save("DevFocusModeOn", false); } catch { }
                DeleteTempDir(dir);
            }
        }

        /// <summary>场景提优的崩溃自愈凭据必须与 CrashGuard.Identify 用同一时间纪元
        /// （QueryProcessSample 的 FILETIME）。此前存 DateTime ticks，两者相差固定常数，
        /// 崩溃后身份校验永远 Mismatch，提优过的进程永不还原。
        /// 自愈消费的是全局注册表账本——测试前后保存/恢复，避免吃掉真机待自愈条目。</summary>
        private static void TestCrashGuardBoostIdentityRoundtrip()
        {
            string dir = NewTempDir("crashguard-units");
            Process probe = null;
            DevFocus dev = null;
            string previousJournal = Settings.LoadStr("Crash_BoostEntriesV2", "");
            try
            {
                Settings.SaveStr("Crash_BoostEntriesV2", "");
                string beat = Path.Combine(dir, "cg.beat");
                probe = StartProbe(beat);
                WaitAdvance(beat, -1, 4000);
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);
                int ioBefore = QueryIoOf(probe.Id);

                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                Eq(true, dev.BoostIdeForTest(probe.Id));
                probe.Refresh();
                Eq(ProcessPriorityClass.AboveNormal, probe.PriorityClass);

                // 模拟崩溃后重启的自愈链：按 PID/创建时间/映像名匹配并还原
                CrashGuard.HealFromCrash();
                probe.Refresh();
                Eq(ProcessPriorityClass.Normal, probe.PriorityClass);
                Eq(ioBefore, QueryIoOf(probe.Id));
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                try { Settings.SaveStr("Crash_BoostEntriesV2", previousJournal); } catch { }
                DeleteTempDir(dir);
            }
        }

        private static void TestBuildCatalogExpandedTools()
        {
            // 新增编译/任务编排工具链：应命中
            Eq(true, BuildCatalog.IsMatch("pnpm"));
            Eq(true, BuildCatalog.IsMatch("yarn"));
            Eq(true, BuildCatalog.IsMatch("bun"));
            Eq(true, BuildCatalog.IsMatch("nx"));
            Eq(true, BuildCatalog.IsMatch("lerna"));
            Eq(true, BuildCatalog.IsMatch("just"));
            Eq(true, BuildCatalog.IsMatch("uv"));
            Eq(true, BuildCatalog.IsMatch("poetry"));
            Eq(true, BuildCatalog.IsMatch("pnpm.exe"));
            // 通用运行时仍排除（防误触发）
            Eq(false, BuildCatalog.IsMatch("node"));
            Eq(false, BuildCatalog.IsMatch("npm"));
            Eq(false, BuildCatalog.IsMatch("npx"));
            Eq(false, BuildCatalog.IsMatch("dotnet"));
            Eq(false, BuildCatalog.IsMatch("java"));
            Eq(false, BuildCatalog.IsMatch("python"));
        }

        // 用户自定义豁免名录：反作弊/加速器自定义清单写入即生效，清除即失效
        private static void TestCustomExemptionCatalogs()
        {
            string savedAc = AntiCheatCatalog.CustomList;
            try
            {
                AntiCheatCatalog.CustomList = "MyAc_Helper; foo.exe";
                Eq(true, AntiCheatCatalog.IsKnownProcess("MyAc_Helper"));
                Eq(true, AntiCheatCatalog.IsKnownProcess("MyAc_Helper.exe"));
                Eq(true, AntiCheatCatalog.IsKnownProcess("foo"));
                Eq(false, AntiCheatCatalog.IsKnownProcess("bar"));
            }
            finally { AntiCheatCatalog.CustomList = savedAc; }
            Eq(false, AntiCheatCatalog.IsKnownProcess("MyAc_Helper"));

            string savedAcc = NetAcceleratorCatalog.CustomList;
            try
            {
                NetAcceleratorCatalog.CustomList = "myacc";
                Eq(true, NetAcceleratorCatalog.IsAcceleratorLikeName("myacc"));
                Eq(true, NetAcceleratorCatalog.IsAcceleratorLikeName("myacc.exe"));
            }
            finally { NetAcceleratorCatalog.CustomList = savedAcc; }
            Eq(false, NetAcceleratorCatalog.IsAcceleratorLikeName("myacc"));

            // 新增常见加速器与内置反作弊命名
            Eq(true, NetAcceleratorCatalog.IsAcceleratorLikeName("steam++"));
            Eq(true, NetAcceleratorCatalog.IsAcceleratorLikeName("ourplay"));
            Eq(true, AntiCheatCatalog.IsKnownProcess("SGuard64.exe"));
        }

        // IDE 自定义名录：写入即生效与内置名录合并；坏行容错；自定义按名匹配（无目录锚点），
        // 内置项仍走安装目录双校验。写 Settings——必须注册在临时存储启用之后。
        private static void TestIdeCatalogCustomList()
        {
            string old = IdeCatalog.CustomList;
            try
            {
                IdeCatalog.CustomList = "notepad; ;\r\nmyide.exe\r\nBad Row  ";
                Eq(true, IdeCatalog.NameMatches("myide"));
                Eq(true, IdeCatalog.NameMatches("myide.exe"));   // .exe 后缀归一
                Eq(true, IdeCatalog.NameMatches("MYIDE"));       // 大小写不敏感
                Eq(true, IdeCatalog.IsMatch("myide", @"C:\ anywhere\myide.exe")); // 无目录锚点
                Eq(false, IdeCatalog.NameMatches(""));           // 空行容错
                Eq(true, IdeCatalog.NameMatches("Bad Row"));     // Trim 容错
                Eq(true, IdeCatalog.NameMatches("code"));        // 内置名录不受影响
                Eq(false, IdeCatalog.IsMatch("code", @"C:\Temp\code.exe")); // 内置双校验照旧
                Eq(true, IdeCatalog.CustomList.Contains("myide.exe")); // 原文保存，展示层原样回显
            }
            finally { IdeCatalog.CustomList = old; }
            Eq(false, IdeCatalog.NameMatches("myide"));          // 还原后失效
        }

        // 专注历史：同日合并增量、Keep 截断、近 N 日补零升序、8 列读写、旧 5 列行兼容。
        // 文件级测试——FilePath 覆写到临时目录，不碰真实数据。
        private static void TestFocusHistoryMergeAndTrim()
        {
            string file = NewTempDir("focus-hist") + "\\focus-history.tsv";
            string old = FocusHistory.FilePath;
            FocusHistory.FilePath = file;
            try
            {
                FocusHistory.AppendOrUpdate(new FocusDayRecord { Day = "2026-09-01", FocusSeconds = 60, FocusSessions = 1 });
                FocusHistory.AppendOrUpdate(new FocusDayRecord { Day = "2026-09-01", FocusSeconds = 30, FocusSessions = 1, Distract = 2, Blocked = 1 });   // 同日合并
                var all = FocusHistory.LoadAll();
                Eq(1, all.Count);
                Eq("2026-09-01", all[0].Day);
                Eq(90L, all[0].FocusSeconds);
                Eq(2, all[0].FocusSessions);
                Eq(2, all[0].Distract);
                Eq(1, all[0].Blocked);

                // 双场景同日合并：日常/编译列累加，与专注列互不干扰
                FocusHistory.AppendOrUpdate(new FocusDayRecord { Day = "2026-09-01", DailySeconds = 300, DailySessions = 1, BuildSeconds = 45 });
                all = FocusHistory.LoadAll();
                Eq(90L, all[0].FocusSeconds);
                Eq(300L, all[0].DailySeconds);
                Eq(1, all[0].DailySessions);
                Eq(45L, all[0].BuildSeconds);

                // Keep 截断：共 66 行（65 天循环 + 09-01），只留最近 60 天
                for (int i = 0; i < 65; i++)
                    FocusHistory.AppendOrUpdate(new FocusDayRecord { Day = new DateTime(2026, 6, 1).AddDays(i).ToString("yyyy-MM-dd"), FocusSeconds = 10, FocusSessions = 1 });
                all = FocusHistory.LoadAll();
                Eq(60, all.Count);
                Eq("2026-06-07", all[0].Day);   // 最旧 6 行（06-01..06-06）被截掉
                Eq("2026-09-01", all[59].Day);
                Eq(90L, all[59].FocusSeconds);  // 合并行数据保留

                // 近 7 日补零：老→新、含今日、缺日补零
                FocusHistory.AppendOrUpdate(new FocusDayRecord { Day = "2026-09-09", FocusSeconds = 10, FocusSessions = 1 });
                var last = FocusHistory.LastDays(7, new DateTime(2026, 9, 11));
                Eq(7, last.Count);
                Eq("2026-09-05", last[0].Day);
                Eq("2026-09-11", last[6].Day);
                Eq(0L, last[0].FocusSeconds);   // 09-05 无数据 → 补零
                Eq(10L, last[4].FocusSeconds);  // 09-09 有数据
                Eq(0L, last[6].FocusSeconds);   // 今日无数据 → 补零

                // 旧 5 列行无损读取：尾部三列补零
                File.WriteAllText(file, "2026-08-01\t120\t2\t3\t1\n");
                all = FocusHistory.LoadAll();
                Eq(1, all.Count);
                Eq(120L, all[0].FocusSeconds);
                Eq(2, all[0].FocusSessions);
                Eq(3, all[0].Distract);
                Eq(1, all[0].Blocked);
                Eq(0L, all[0].DailySeconds);
                Eq(0, all[0].DailySessions);
                Eq(0L, all[0].BuildSeconds);
            }
            finally { FocusHistory.FilePath = old; DeleteTempDir(Path.GetDirectoryName(file)); }
        }

        // 专注历史：会话与分心命中经 FocusStats 写当日趋势（注册表今日键 + TSV 双写、日切归零）。
        // 写 Settings——必须注册在临时存储启用之后。
        private static void TestFocusStatsFeedsHistory()
        {
            string file = NewTempDir("focus-stats") + "\\focus-history.tsv";
            string old = FocusHistory.FilePath;
            FocusHistory.FilePath = file;
            FocusStats.ResetForTest();
            try
            {
                var day = new DateTime(2026, 9, 11, 10, 0, 0);
                FocusStats.RecordSession(90 * TimeSpan.TicksPerSecond, day);
                FocusStats.RecordSession(30 * TimeSpan.TicksPerSecond, day);
                Eq(120L, FocusStats.TodaySeconds(day));
                Eq(2, FocusStats.TodaySessions(day));

                FocusStats.RecordDistract(false, day);
                FocusStats.RecordDistract(true, day);
                Eq(2, FocusStats.TodayDistract(day));
                Eq(1, FocusStats.TodayBlocked(day));

                var all = FocusHistory.LoadAll();
                Eq(1, all.Count);
                Eq("2026-09-11", all[0].Day);
                Eq(120L, all[0].FocusSeconds);
                Eq(2, all[0].FocusSessions);
                Eq(2, all[0].Distract);
                Eq(1, all[0].Blocked);

                // 日切归零：次日会话从零起算，历史新增一行
                var next = day.AddDays(1);
                FocusStats.RecordSession(60 * TimeSpan.TicksPerSecond, next);
                Eq(60L, FocusStats.TodaySeconds(next));
                Eq(0, FocusStats.TodayDistract(next));
                all = FocusHistory.LoadAll();
                Eq(2, all.Count);
                Eq(60L, all[1].FocusSeconds);
            }
            finally
            {
                FocusHistory.FilePath = old;
                FocusStats.ResetForTest();
                DeleteTempDir(Path.GetDirectoryName(file));
            }
        }


        // 编译统计：时长经 FocusStats 写今日键与 TSV buildSec 列，日切归零
        private static void TestFocusBuildRecorded()
        {
            string file = NewTempDir("focus-build") + "\\focus-history.tsv";
            string old = FocusHistory.FilePath;
            FocusHistory.FilePath = file;
            FocusStats.ResetForTest();
            try
            {
                var day = new DateTime(2026, 9, 11, 10, 0, 0);
                FocusStats.RecordBuild(150 * TimeSpan.TicksPerSecond, day);
                FocusStats.RecordBuild(30 * TimeSpan.TicksPerSecond, day);
                Eq(180L, FocusStats.TodayBuildSeconds(day));
                Eq(2, FocusStats.TodayBuildSessions(day));
                var all = FocusHistory.LoadAll();
                Eq(1, all.Count);
                Eq(180L, all[0].BuildSeconds);
                Eq(0L, all[0].FocusSeconds);   // 编译列与专注列独立

                var next = day.AddDays(1);
                FocusStats.RecordBuild(60 * TimeSpan.TicksPerSecond, next);
                Eq(60L, FocusStats.TodayBuildSeconds(next));
                all = FocusHistory.LoadAll();
                Eq(2, all.Count);
                Eq(180L, all[0].BuildSeconds);
                Eq(60L, all[1].BuildSeconds);
            }
            finally
            {
                FocusHistory.FilePath = old;
                FocusStats.ResetForTest();
                DeleteTempDir(Path.GetDirectoryName(file));
            }
        }

        // 专注目标解析：30-1440 之外或非法一律回落默认 240（纯逻辑）
        private static void TestFocusGoalParse()
        {
            Eq(240, FocusStats.ParseGoalMinutes("240"));
            Eq(30, FocusStats.ParseGoalMinutes("30"));
            Eq(1440, FocusStats.ParseGoalMinutes("1440"));
            Eq(240, FocusStats.ParseGoalMinutes("29"));
            Eq(240, FocusStats.ParseGoalMinutes("1441"));
            Eq(240, FocusStats.ParseGoalMinutes("abc"));
            Eq(240, FocusStats.ParseGoalMinutes(""));
            Eq(240, FocusStats.ParseGoalMinutes(null));
        }

        // 编译统计集成：真实编译进程起止（探针扮演 msbuild）经 DevFocus 起止跟踪落盘
        private static void TestDevFocusRecordsBuildStats()
        {
            string dir = NewTempDir("devfocus-buildstats");
            Process probe = null;
            DevFocus dev = null;
            FocusStats.ResetForTest();
            try
            {
                var arbiter = new ScenarioArbiter();
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                dev = new DevFocus(arbiter, core, () => true, (n, p) => false, name => false);

                string beat;
                probe = StartNamedProbe(dir, "msbuild.exe", out beat);
                WaitAdvance(beat, -1, 4000);
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Started) }, false));
                Eq(true, dev.IsGranted);
                Thread.Sleep(1200);   // 编译进行 1 秒以上
                dev.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "msbuild", ProcessChangeKind.Stopped) }, false));
                Eq(1, FocusStats.TodayBuildSessions(DateTime.Now));
                Eq(true, FocusStats.TodayBuildSeconds(DateTime.Now) >= 1);
            }
            finally
            {
                if (dev != null) try { dev.Stop(); } catch { }
                if (probe != null) try { StopOwned(probe); } catch { }
                FocusStats.ResetForTest();
                DeleteTempDir(dir);
            }
        }


        // 编译起止守卫（纯逻辑）：无起点一律记 0——初始扫描加入的进程在第一批事件里结束时
        // 若 buildStartTicks 未置位，旧实现会计出天文时长写爆今日统计
        private static void TestBuildEndedElapsedGuard()
        {
            Eq(0L, DevFocus.BuildEndedElapsed(0, 999999));            // 无起点：不记
            Eq(0L, DevFocus.BuildEndedElapsed(-5, 999999));           // 非法起点：不记
            Eq(0L, DevFocus.BuildEndedElapsed(500, 100));             // 时钟回拨：不记负值
            Eq(150L, DevFocus.BuildEndedElapsed(100, 250));           // 正常起止
        }

        // 分心策略真值表：未掌权/未开专注不动作；提醒按名去重；阻断开关把动作升级为阻断
        // （阻断不去重——用户手滑再开分心应用仍会被关回去，但不会被气球刷屏）；
        // 守护服务清单优先——命中服务的名字绝不按分心处理（否则自动拉起与阻断互相残杀成死循环）
        private static void TestDistractActionPolicy()
        {
            Eq(DistractAction.None, DevFocus.DecideDistractAction(false, true, false, false, false));
            Eq(DistractAction.None, DevFocus.DecideDistractAction(true, false, false, false, false));
            Eq(DistractAction.None, DevFocus.DecideDistractAction(true, true, true, false, false));
            Eq(DistractAction.NotifyOnly, DevFocus.DecideDistractAction(true, true, false, false, false));
            Eq(DistractAction.NotifyAndBlock, DevFocus.DecideDistractAction(true, true, false, true, false));
            Eq(DistractAction.BlockAgain, DevFocus.DecideDistractAction(true, true, true, true, false));

            // 服务优先：即使掌权+专注+阻断全开、且名字在分心清单里，服务名一律不动作
            Eq(DistractAction.None, DevFocus.DecideDistractAction(true, true, false, true, true));
            Eq(DistractAction.None, DevFocus.DecideDistractAction(true, true, true, true, true));
            Eq(DistractAction.None, DevFocus.DecideDistractAction(false, false, false, false, true));
        }

        // 阻断气球限频：30 秒/名，未记录过立即允许
        private static void TestDistractBlockBalloonRateLimit()
        {
            long interval = 30L * TimeSpan.TicksPerSecond;
            Eq(true, DevFocus.BlockBalloonReady(0, 1000));                     // 无记录：允许
            Eq(false, DevFocus.BlockBalloonReady(1000, 1000 + interval - 1));  // 未到期
            Eq(true, DevFocus.BlockBalloonReady(1000, 1000 + interval));       // 恰好到期
            Eq(true, DevFocus.BlockBalloonReady(1000, 1000 + interval * 5));   // 远超
        }

        // 分心按名统计：.exe/大小写归一合并、同数按名序稳定、Top8 截断、日切清空
        private static void TestFocusStatsDistractNames()
        {
            string file = NewTempDir("focus-names") + "\\focus-history.tsv";
            string old = FocusHistory.FilePath;
            FocusHistory.FilePath = file;
            FocusStats.ResetForTest();
            try
            {
                var day = new DateTime(2026, 9, 11, 10, 0, 0);
                FocusStats.RecordDistract(false, "discord.exe", day);   // .exe 归一
                FocusStats.RecordDistract(true, "Discord", day);        // 大小写合并
                FocusStats.RecordDistract(false, "steam", day);
                FocusStats.RecordDistract(false, "steam", day);
                Eq(4, FocusStats.TodayDistract(day));
                Eq("discord×2 · steam×2", FocusStats.TodayDistractTopText(day));

                // Top8 截断：再加 9 个名字，只留次数最高的 8 个（discord/steam×2 优先）
                for (int i = 1; i <= 9; i++)
                    FocusStats.RecordDistract(false, "app" + i, day);
                string top = FocusStats.TodayDistractTopText(day);
                Eq(8, top.Split(new[] { " · " }, StringSplitOptions.None).Length);
                Eq(true, top.StartsWith("discord×2 · steam×2 · app1×1")); // 同数按名序
                Eq(true, top.Contains("app6×1"));
                Eq(false, top.Contains("app7"));                          // app7..app9 被截掉

                // 日切清空
                var next = day.AddDays(1);
                FocusStats.RecordDistract(false, "other", next);
                Eq("other×1", FocusStats.TodayDistractTopText(next));
            }
            finally
            {
                FocusHistory.FilePath = old;
                FocusStats.ResetForTest();
                DeleteTempDir(Path.GetDirectoryName(file));
            }
        }

        private static void TestIdeCatalogDbTools()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Eq(true, IdeCatalog.IsMatch("datagrip64",
                System.IO.Path.Combine(pf, @"JetBrains\DataGrip\bin\datagrip64.exe")));
            Eq(false, IdeCatalog.IsMatch("datagrip64", @"C:\Temp\datagrip64.exe"));
            Eq(true, IdeCatalog.IsMatch("dbeaver",
                System.IO.Path.Combine(pf, @"DBeaver\dbeaver.exe")));
            Eq(true, IdeCatalog.IsMatch("ssms",
                System.IO.Path.Combine(pf, @"Microsoft SQL Server Management Studio\ssms.exe")));
            Eq(true, IdeCatalog.IsMatch("studio64",
                System.IO.Path.Combine(pf, @"Android\Android Studio\bin\studio64.exe")));
            Eq(true, IdeCatalog.IsMatch("azuredatastudio",
                System.IO.Path.Combine(local, @"Programs\Azure Data Studio\azuredatastudio.exe")));
            Eq(true, IdeCatalog.IsMatch("mysqlworkbench",
                System.IO.Path.Combine(pf, @"MySQL\MySQL Workbench\MySQLWorkbench.exe")));
        }
    }
}
