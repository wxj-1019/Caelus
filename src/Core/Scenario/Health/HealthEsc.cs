// @author zenjiro 18967498922@163.com
// 文件用途 TSV 转义共享助手：StartupAudit 基线与维护历史共用的单趟转义/还原

using System.Text;

namespace CaelusApp
{
    internal static class HealthEsc
    {
        public static string Esc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        /// <summary>转义是歧义的（"\\t" 既可能是字面反斜杠+t，也可能是转义后的 TAB），
        /// 连续 Replace 无法正确处理，必须单趟从左到右扫描。</summary>
        public static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    if (next == '\\') { sb.Append('\\'); i++; continue; }
                    if (next == 't') { sb.Append('\t'); i++; continue; }
                    if (next == 'r') { sb.Append('\r'); i++; continue; }
                    if (next == 'n') { sb.Append('\n'); i++; continue; }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
