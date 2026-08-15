// @author zenjiro 18967498922@163.com
// 文件用途 开发服务守护的自测：名录解析、退出通知去重、死 PID 兜底清理、压制豁免

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestDevServiceCatalogMatch()
        {
            Settings.SaveStr("DevServiceList", "node;redis-server\r\nmysql");
            DevServiceCatalog.Reload();
            try
            {
                Eq(true, DevServiceCatalog.IsMatch("node"));
                Eq(true, DevServiceCatalog.IsMatch("node.exe"));
                Eq(true, DevServiceCatalog.IsMatch("NODE"));
                Eq(true, DevServiceCatalog.IsMatch("redis-server"));
                Eq(true, DevServiceCatalog.IsMatch("mysql"));
                Eq(false, DevServiceCatalog.IsMatch("python"));
                Eq(false, DevServiceCatalog.IsMatch(null));
                Eq(false, DevServiceCatalog.IsMatch(""));
            }
            finally
            {
                Settings.SaveStr("DevServiceList", "");
                DevServiceCatalog.Reload();
            }
        }

        private static void TestDevServiceGuardNotifyOnLastStop()
        {
            string dir = NewTempDir("devsvc-stop");
            Process probe = null;
            DevServiceGuard guard = null;
            try
            {
                Settings.SaveStr("DevServiceList", "node");
                DevServiceCatalog.Reload();
                DevServiceGuard.MinAliveTicks = 0;
                guard = new DevServiceGuard();
                var stopped = new List<string>();
                guard.ServiceStopped += name => stopped.Add(name);

                string beat;
                probe = StartNamedProbe(dir, "node.exe", out beat);
                WaitAdvance(beat, -1, 4000);

                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "node", ProcessChangeKind.Started) }, false));
                Eq(1, guard.LiveCount);

                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(probe.Id, "node", ProcessChangeKind.Stopped) }, false));
                Eq(0, guard.LiveCount);
                Eq(1, stopped.Count);
                Eq("node", stopped[0]);
            }
            finally
            {
                if (guard != null) guard.Stop();
                if (probe != null) try { StopOwned(probe); } catch { }
                Settings.SaveStr("DevServiceList", "");
                DevServiceCatalog.Reload();
                DevServiceGuard.MinAliveTicks = 3L * TimeSpan.TicksPerSecond;
                DeleteTempDir(dir);
            }
        }

        private static void TestDevServiceGuardNoFireWhileOthersAlive()
        {
            string dir1 = NewTempDir("devsvc-multi1");
            string dir2 = NewTempDir("devsvc-multi2");
            Process p1 = null, p2 = null;
            DevServiceGuard guard = null;
            try
            {
                Settings.SaveStr("DevServiceList", "node");
                DevServiceCatalog.Reload();
                DevServiceGuard.MinAliveTicks = 0;
                guard = new DevServiceGuard();
                var stopped = new List<string>();
                guard.ServiceStopped += name => stopped.Add(name);

                string beat1, beat2;
                p1 = StartNamedProbe(dir1, "node.exe", out beat1);
                p2 = StartNamedProbe(dir2, "node.exe", out beat2);
                WaitAdvance(beat1, -1, 4000);
                WaitAdvance(beat2, -1, 4000);

                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[]
                    {
                        MakeChange(p1.Id, "node", ProcessChangeKind.Started),
                        MakeChange(p2.Id, "node", ProcessChangeKind.Started)
                    }, false));
                Eq(2, guard.LiveCount);

                // 退出其中一个实例：计数未归零，不触发
                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(p1.Id, "node", ProcessChangeKind.Stopped) }, false));
                Eq(1, guard.LiveCount);
                Eq(0, stopped.Count);

                // 最后一个退出：触发一次
                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(p2.Id, "node", ProcessChangeKind.Stopped) }, false));
                Eq(0, guard.LiveCount);
                Eq(1, stopped.Count);
                Eq("node", stopped[0]);
            }
            finally
            {
                if (guard != null) guard.Stop();
                if (p1 != null) try { StopOwned(p1); } catch { }
                if (p2 != null) try { StopOwned(p2); } catch { }
                Settings.SaveStr("DevServiceList", "");
                DevServiceCatalog.Reload();
                DevServiceGuard.MinAliveTicks = 3L * TimeSpan.TicksPerSecond;
                DeleteTempDir(dir1);
                DeleteTempDir(dir2);
            }
        }

        private static void TestDevServiceGuardPrunesDeadPid()
        {
            Settings.SaveStr("DevServiceList", "node");
            DevServiceCatalog.Reload();
            DevServiceGuard.MinAliveTicks = 0;
            var guard = new DevServiceGuard();
            var stopped = new List<string>();
            guard.ServiceStopped += name => stopped.Add(name);
            try
            {
                // 假 PID（永不存活）：Stopped 事件丢失时靠兜底清理移除并通知
                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(999999, "node", ProcessChangeKind.Started) }, false));
                Eq(1, guard.LiveCount);

                guard.NotifyProcessChanges(new ProcessChangeBatch(new ProcessChange[0], false));
                Eq(0, guard.LiveCount);
                Eq(1, stopped.Count);
                Eq("node", stopped[0]);
            }
            finally
            {
                guard.Stop();
                Settings.SaveStr("DevServiceList", "");
                DevServiceCatalog.Reload();
                DevServiceGuard.MinAliveTicks = 3L * TimeSpan.TicksPerSecond;
            }
        }

        private static void TestDevServiceExemptFromSuppression()
        {
            string winRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            int self = Process.GetCurrentProcess().Id;
            int session = Process.GetCurrentProcess().SessionId;
            var noWindows = new HashSet<int>();
            Settings.SaveStr("DevServiceList", "node");
            DevServiceCatalog.Reload();
            try
            {
                // 注册的开发服务：豁免（模拟 Program.cs 的 isWhitelisted 包装：白名单 OR 开发服务）
                Eq(false, DevFocus.ShouldSuppressBackground(
                    5000, self, "node", @"C:\node\node.exe",
                    session, session, 0, noWindows, winRoot,
                    (n, p) => DevServiceCatalog.IsMatch(n)));
                // 未注册的普通后台：仍应压制
                Eq(true, DevFocus.ShouldSuppressBackground(
                    5000, self, "someapp", @"C:\Apps\someapp.exe",
                    session, session, 0, noWindows, winRoot,
                    (n, p) => DevServiceCatalog.IsMatch(n)));
            }
            finally
            {
                Settings.SaveStr("DevServiceList", "");
                DevServiceCatalog.Reload();
            }
        }
    }
}
