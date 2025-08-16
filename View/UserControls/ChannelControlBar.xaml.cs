using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for ChannelControlBar.xaml
    /// </summary>
    public partial class ChannelControlBar : UserControl
    {
        public ObservableCollection<ChannelControl> ChannelControls { get; } = new();

        public ChannelControlBar()
        {
            InitializeComponent();
            ChannelItemsControl.ItemsSource = ChannelControls;
        }

        // Helper to add a channel control
        public void AddChannel(ChannelControl control)
        {
            ChannelControls.Add(control);
        }

        // Helper to remove a channel control
        public void RemoveChannel(ChannelControl control)
        {
            ChannelControls.Remove(control);
        }
    }
}
