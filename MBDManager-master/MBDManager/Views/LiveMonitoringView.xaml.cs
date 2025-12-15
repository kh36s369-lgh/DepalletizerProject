using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MBDManager.ViewModels;

namespace MBDManager.Views
{
    /// <summary>
    /// LiveMonitoringView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class LiveMonitoringView : UserControl
    {
        public LiveMonitoringView()
        {
            InitializeComponent();

            //this.Unloaded += LiveMonitoringView_Unloaded;

            //var vm = new LiveMonitoringViewModel(Dispatcher);
            //DataContext = vm;
        }
        private void LiveMonitoringView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is IDisposable disposableViewModel)
            {
                disposableViewModel.Dispose();
            }
        }
    }
}
