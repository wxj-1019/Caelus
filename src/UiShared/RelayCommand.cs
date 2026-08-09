// @author zenjiro 18967498922@163.com
// 文件用途 MVVM 基座：无参 ICommand 实现

using System;
using System.Windows.Input;

namespace CaelusApp
{
    internal sealed class RelayCommand : ICommand
    {
        private readonly Action run;
        private readonly Func<bool> can;

        public RelayCommand(Action run) : this(run, null) { }

        public RelayCommand(Action run, Func<bool> can)
        {
            if (run == null) throw new ArgumentNullException("run");
            this.run = run;
            this.can = can;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return can == null || can();
        }

        public void Execute(object parameter)
        {
            if (CanExecute(parameter)) run();
        }

        public void RaiseCanExecuteChanged()
        {
            EventHandler h = CanExecuteChanged;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
