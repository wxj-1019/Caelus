// @author zenjiro 18967498922@163.com
// 文件用途 UiShared 表现层逻辑与 WPF 解耦点的自测

using System;

namespace CaelusApp
{
    internal static partial class SelfTests
    {
        private static void TestNativeLightModeHook()
        {
            Func<bool> prev = Native.LightModeQuery;
            try
            {
                Native.LightModeQuery = null;
                Eq(false, Native.QueryLightMode());
                Native.LightModeQuery = () => true;
                Eq(true, Native.QueryLightMode());
                Native.LightModeQuery = () => false;
                Eq(false, Native.QueryLightMode());
            }
            finally { Native.LightModeQuery = prev; }
        }
    }
}
