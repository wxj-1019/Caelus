// @author zenjiro 18967498922@163.com
// 文件用途 以先写临时文件再替换的方式保证配置和日志不会被写坏

using System;
using System.IO;
using System.Text;

namespace CaelusApp
{

    internal static class AtomicFile
    {
        public static bool WriteLines(string path, string[] lines, string label)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string tmp = path + ".tmp";
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            bool tmpComplete = false;
            try
            {
                File.WriteAllLines(tmp, lines ?? new string[0], new UTF8Encoding(false));
                tmpComplete = true;
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {

                // File.Replace 失败时（通常因目标文件被独占，如杀软扫描），
                // 用 File.Copy 尽力把已写完整的 tmp 落盘。Copy 本身非原子——
                // 写到一半崩溃会留下半截 path——但这是 Replace 不可用时的降级，
                // 取“数据落盘”优先于“绝对原子”。若需更强保证，调用方应重试。
                try
                {
                    if (tmpComplete && File.Exists(tmp))
                    {
                        File.Copy(tmp, path, true);
                        try { File.Delete(tmp); } catch { }
                        return true;
                    }
                }
                catch { }
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                Logger.LogFailure(label + "写入失败", ex);
                return false;
            }
        }
    }
}
