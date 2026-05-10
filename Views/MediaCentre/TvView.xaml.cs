using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;

namespace AtlasAI.Views.MediaCentre
{
    public partial class TvView : UserControl
    {
        public TvView()
        {
            InitializeComponent();
        }

        private void PosterCard_RightClickOpenMenu(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is not Button btn)
                    return;

                var menu = btn.ContextMenu;
                if (menu == null)
                    return;

                menu.PlacementTarget = btn;
                menu.Placement = PlacementMode.MousePoint;
                menu.IsOpen = true;
                e.Handled = true;
            }
            catch
            {
            }
        }
    }
}
