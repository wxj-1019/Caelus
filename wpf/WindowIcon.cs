// @author zenjiro 18967498922@163.com
// 文件用途 从 IconArt 生成窗口图标（任务栏 / Alt-Tab），主窗口与启动屏共用

using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaelusApp.WpfHost
{
    internal static class WindowIcon
    {
        // 失败时返回 null（窗口保持系统默认图标，不影响运行）
        public static ImageSource Create()
        {
            try
            {
                using (System.Drawing.Icon icon = IconArt.MakeMultiIcon())
                {
                    return Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                }
            }
            catch { return null; }
        }
    }
}
