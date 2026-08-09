// @author zenjiro 18967498922@163.com
// 文件用途 MVVM 基座：属性变更通知（供 WPF 绑定与自测共用）

using System.ComponentModel;

namespace CaelusApp
{
    internal abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, string name)
        {
            if (object.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        protected void Raise(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
