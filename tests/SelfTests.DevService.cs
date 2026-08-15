// @author zenjiro 18967498922@163.com
// 文件用途 开发服务守护的自测：名录解析、退出通知去重、压制豁免

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
            Settings.SaveStr("DevServiceList", "node;redis-server");
            DevServiceCatalog.Reload();
            DevServiceGuard.MinAliveTicks = 0;   // 测试：立即触发
            var guard = new DevServiceGuard();
            var stopped = new List<string>();
            guard.ServiceStopped += name => stopped.Add(name);
            try
            {
                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(100, "node", ProcessChangeKind.Started) }, false));
                Eq(1, guard.LiveCount);

                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(100, "node", ProcessChangeKind.Stopped) }, false));
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

        private static void TestDevServiceGuardNoFireWhileOthersAlive()
        {
            Settings.SaveStr("DevServiceList", "node");
            DevServiceCatalog.Reload();
            DevServiceGuard.MinAliveTicks = 0;
            var guard = new DevServiceGuard();
            var stopped = new List<string>();
            guard.ServiceStopped += name => stopped.Add(name);
            try
            {
                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[]
                    {
                        MakeChange(101, "node", ProcessChangeKind.Started),
                        MakeChange(102, "node", ProcessChangeKind.Started)
                    }, false));
                Eq(2, guard.LiveCount);

                // 退出其中一个实例：计数未归零，不触发
                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(101, "node", ProcessChangeKind.Stopped) }, false));
                Eq(1, guard.LiveCount);
                Eq(0, stopped.Count);

                // 最后一个退出：触发一次
                guard.NotifyProcessChanges(new ProcessChangeBatch(
                    new[] { MakeChange(102, "node", ProcessChangeKind.Stopped) }, false));
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
