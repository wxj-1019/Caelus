// @author zenjiro 18967498922@163.com
// 文件用途 维护动作注册表：动作注册制，调度/历史/UI 与具体动作解耦

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal sealed class HealthActionCatalog
    {
        private readonly List<IHealthAction> all = new List<IHealthAction>();

        public void Register(IHealthAction action)
        {
            if (action == null) throw new ArgumentNullException("action");
            all.Add(action);
        }

        public IList<IHealthAction> All { get { return all; } }

        public IHealthAction Find(string id)
        {
            if (id == null) return null;
            foreach (IHealthAction a in all)
                if (string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }
    }
}
