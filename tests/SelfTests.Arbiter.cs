// @author zenjiro 18967498922@163.com
// 文件用途 场景仲裁器的自测：优先级求胜、抢占顺序、挂起补位、事件通知

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private sealed class FakeScenario : IScenario
        {
            public ScenarioKind Kind { get; private set; }
            public int Priority { get; private set; }
            public readonly List<string> Calls = new List<string>();

            public FakeScenario(ScenarioKind kind, int priority)
            {
                Kind = kind;
                Priority = priority;
            }

            public void Grant() { Calls.Add("G:" + Kind); }
            public void Suspend() { Calls.Add("S:" + Kind); }
        }

        private static ScenarioArbiter NewArbiterWithAll(
            out FakeScenario game, out FakeScenario dev, out FakeScenario daily)
        {
            var arbiter = new ScenarioArbiter();
            game = new FakeScenario(ScenarioKind.Game, 100);
            dev = new FakeScenario(ScenarioKind.DevFocus, 50);
            daily = new FakeScenario(ScenarioKind.DailyCare, 10);
            arbiter.Register(game);
            arbiter.Register(dev);
            arbiter.Register(daily);
            return arbiter;
        }

        // 注意：与 CurrentGranted（ScenarioKind?）比较必须显式 Eq<ScenarioKind?>——
        // Eq<T> 两个参数类型不同（ScenarioKind vs ScenarioKind?）会让泛型推断失败（CS0411）
        private static void TestArbiterSingleActivation()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            Eq<ScenarioKind?>(null, arbiter.CurrentGranted);

            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);
            Eq(1, dev.Calls.Count);
            Eq("G:DevFocus", dev.Calls[0]);
        }

        private static void TestArbiterPreemptionOrder()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);

            arbiter.ReportActivity(ScenarioKind.Game, true);
            Eq<ScenarioKind?>(ScenarioKind.Game, arbiter.CurrentGranted);
            // 先挂起旧掌权者，再授权新掌权者（顺序是要害）
            Eq(2, dev.Calls.Count);
            Eq("S:DevFocus", dev.Calls[1]);
            Eq(1, game.Calls.Count);
            Eq("G:Game", game.Calls[0]);
        }

        private static void TestArbiterResumeAfterPreemption()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            arbiter.ReportActivity(ScenarioKind.Game, true);

            arbiter.ReportActivity(ScenarioKind.Game, false);
            // 游戏退出后开发场景补位恢复（它的检测状态仍在）
            Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);
            Eq("G:DevFocus", dev.Calls[2]);
        }

        private static void TestArbiterLowPriorityNoPreempt()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);

            arbiter.ReportActivity(ScenarioKind.DailyCare, true);
            Eq<ScenarioKind?>(ScenarioKind.DevFocus, arbiter.CurrentGranted);
            Eq(0, daily.Calls.Count);
        }

        private static void TestArbiterEmptyGrantsNull()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            arbiter.ReportActivity(ScenarioKind.DevFocus, false);
            Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            Eq("S:DevFocus", dev.Calls[1]);
        }

        private static void TestArbiterDuplicateReportNoOp()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            arbiter.ReportActivity(ScenarioKind.Game, false);
            Eq(1, dev.Calls.Count);
            Eq(0, game.Calls.Count);
        }

        private static void TestArbiterGrantedChangedEvent()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            var seen = new List<ScenarioKind?>();
            arbiter.GrantedChanged += k => seen.Add(k);

            arbiter.ReportActivity(ScenarioKind.DevFocus, true);
            arbiter.ReportActivity(ScenarioKind.DevFocus, false);
            Eq(2, seen.Count);
            Eq<ScenarioKind?>(ScenarioKind.DevFocus, seen[0]);
            Eq<ScenarioKind?>(null, seen[1]);
        }

        private static void TestArbiterUnregisteredKindIgnored()
        {
            var arbiter = new ScenarioArbiter();
            var late = new FakeScenario(ScenarioKind.DailyCare, 10);
            arbiter.ReportActivity(ScenarioKind.DailyCare, true);
            Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            arbiter.Register(late);
            // 注册后仍为空：未注册期间的报告只记账，需再次报告才成为候选
            Eq<ScenarioKind?>(null, arbiter.CurrentGranted);
            Eq(0, late.Calls.Count);
            arbiter.ReportActivity(ScenarioKind.DailyCare, true);
            Eq<ScenarioKind?>(ScenarioKind.DailyCare, arbiter.CurrentGranted);
            Eq(1, late.Calls.Count);
            Eq("G:DailyCare", late.Calls[0]);
        }

        private static void TestArbiterConcurrentReports()
        {
            FakeScenario game, dev, daily;
            var arbiter = NewArbiterWithAll(out game, out dev, out daily);
            var notifyGate = new object();
            ScenarioKind? lastNotified = null;
            arbiter.GrantedChanged += k => { lock (notifyGate) lastNotified = k; };

            var t1 = new Thread(delegate()
            {
                for (int i = 0; i < 2000; i++)
                    arbiter.ReportActivity(ScenarioKind.Game, i % 2 == 0);
            });
            var t2 = new Thread(delegate()
            {
                for (int i = 0; i < 2000; i++)
                    arbiter.ReportActivity(ScenarioKind.DevFocus, i % 2 == 1);
            });
            t1.Start();
            t2.Start();
            t1.Join();
            t2.Join();

            ScenarioKind? finalGranted = arbiter.CurrentGranted;
            // 不变量：每个场景的调用序列为空，或从 G 开始严格 G/S 交替（偶数位 G、奇数位 S）
            FakeScenario[] fakes = new FakeScenario[] { game, dev, daily };
            foreach (FakeScenario f in fakes)
            {
                for (int i = 0; i < f.Calls.Count; i++)
                {
                    string expected = ((i % 2 == 0) ? "G:" : "S:") + f.Kind;
                    Eq(expected, f.Calls[i]);
                }
            }
            // 当前掌权者必以 G 结尾（调用数为奇数），其余为空或以 S 结尾（调用数为偶数）
            Eq(finalGranted.HasValue && fakes[(int)finalGranted.Value].Calls.Count % 2 == 1, true);
            for (int i = 0; i < fakes.Length; i++)
            {
                if (finalGranted.HasValue && i == (int)finalGranted.Value) continue;
                Eq(fakes[i].Calls.Count % 2 == 0, true);
            }
            // 最终记账与最后一次派发/通知一致
            lock (notifyGate) Eq<ScenarioKind?>(finalGranted, lastNotified);
        }

        private static void TestGameModeActiveChangedEvent()
        {
            string dir = NewTempDir("arbiter-gm");
            try
            {
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                var gm = new GameMode(dir, core);
                var seen = new List<bool>();
                gm.ActiveChanged += on => seen.Add(on);

                gm.SimulateActiveForTest(true);
                gm.SimulateActiveForTest(false);
                Eq(2, seen.Count);
                Eq(true, seen[0]);
                Eq(false, seen[1]);
            }
            finally { DeleteTempDir(dir); }
        }

        private static void TestGameModeWhitelistQueryEmpty()
        {
            string dir = NewTempDir("arbiter-wl");
            try
            {
                var core = new SuppressionCore(Path.Combine(dir, "s.state"));
                var gm = new GameMode(dir, core);
                // 空白名单下任何进程都不被豁免
                Eq(false, gm.IsProcessWhitelisted("chrome", @"C:\Apps\chrome.exe"));
            }
            finally { DeleteTempDir(dir); }
        }
    }
}
