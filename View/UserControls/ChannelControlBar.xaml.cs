using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace SerialPlotDN_WPF.View.UserControls
{
    /// <summary>
    /// Interaction logic for ChannelControlBar.xaml
    /// </summary>
    public partial class ChannelControlBar : UserControl
    {
        public ObservableCollection<ChannelControl> Channels { get; } = new();

        public ChannelControlBar()
        {
            InitializeComponent();
            ChannelItemsControl.ItemsSource = Channels;
        }

        // Helper to add a channel control
        public void AddChannel(ChannelControl control)
        {
            Channels.Add(control);
        }

        // Helper to remove a channel control
        public void RemoveChannel(ChannelControl control)
        {
            Channels.Remove(control);
        }
    }
}
