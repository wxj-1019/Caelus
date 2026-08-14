// @author zenjiro 18967498922@163.com
// 文件用途 场景仲裁器的自测：优先级求胜、抢占顺序、挂起补位、事件通知

using System;
using System.Collections.Generic;

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
    }
}
