using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
 
namespace AtlasAI.Views.MediaCentre
{
    public partial class MoviesView : UserControl
    {
        public MoviesView()
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
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
                e.Handled = true;
            }
            catch
            {
            }
        }
    }
}
