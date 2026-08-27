// @author zenjiro 18967498922@163.com
// 文件用途 清单类 tweak（MSI/网卡省电/Nagle）的设备清单合并：并集去重，禁止覆盖既有条目

using System;
using System.Collections.Generic;

namespace CaelusApp
{
    internal static class TweakDeviceList
    {
        /// <summary>把本轮改动并入既有清单：保留既有顺序与条目（含此前还原失败
        /// 留下的条目），新增项追加并去重。覆盖式写回会让"已改过故本轮跳过"的
        /// 设备从清单脱落，其改动与备份永久孤儿化。</summary>
        internal static List<string> Merge(string[] existing, IEnumerable<string> additions)
        {
            var merged = new List<string>();
            if (existing != null)
                foreach (string id in existing)
                    if (!string.IsNullOrEmpty(id) && !merged.Contains(id)) merged.Add(id);
            if (additions != null)
                foreach (string id in additions)
                    if (!string.IsNullOrEmpty(id) && !merged.Contains(id)) merged.Add(id);
            return merged;
        }
    }
}
