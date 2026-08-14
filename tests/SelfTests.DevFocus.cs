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
                dev = new DevFocus(arbiter, core, () => true);

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
                if (probe != null) StopOwned(probe);
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
                dev = new DevFocus(arbiter, core, () => true);

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
                if (probe != null) StopOwned(probe);
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
                dev = new DevFocus(arbiter, core, () => on);

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
                if (probe != null) StopOwned(probe);
                DeleteTempDir(dir);
            }
        }
    }
}
