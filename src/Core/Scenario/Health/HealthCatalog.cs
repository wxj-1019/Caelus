// @author zenjiro 18967498922@163.com
// 文件用途 默认维护动作目录：当前注册着色器缓存清理与启动项审查；新动作在此加一行

namespace CaelusApp
{
    internal static class HealthCatalog
    {
        private static readonly object lk = new object();
        private static HealthActionCatalog shared;

        public static HealthActionCatalog Shared
        {
            get
            {
                lock (lk)
                {
                    if (shared == null)
                    {
                        var c = new HealthActionCatalog();
                        c.Register(new ShaderCacheAction());
                        c.Register(new StartupAuditAction());
                        shared = c;
                    }
                    return shared;
                }
            }
        }
    }
}
