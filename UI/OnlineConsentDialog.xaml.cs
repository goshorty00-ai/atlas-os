using System.Windows;
using System.Windows.Input;
using AtlasAI.Core;

namespace AtlasAI.UI
{
    /// <summary>
    /// Online access consent dialog.
    /// Calm, optional, reversible consent language.
    /// </summary>
    public partial class OnlineConsentDialog : Window
    {
        public OnlineConsentResult Result { get; private set; } = new() { Decision = OnlineConsentDecision.Denied };

        public OnlineConsentDialog()
        {
            InitializeComponent();
            
            // Allow dragging the window
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };
            
            // ESC to close (deny)
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Result = new OnlineConsentResult { Decision = OnlineConsentDecision.Denied };
                    DialogResult = false;
                    Close();
                }
            };
        }

        private void AllowOnce_Click(object sender, RoutedEventArgs e)
        {
            Result = new OnlineConsentResult { Decision = OnlineConsentDecision.AllowOnce };
            DialogResult = true;
            Close();
        }

        private void AllowDuration_Click(object sender, RoutedEventArgs e)
        {
            Result = new OnlineConsentResult 
            { 
                Decision = OnlineConsentDecision.AllowForSession
            };
            DialogResult = true;
            Close();
        }

        private void NotNow_Click(object sender, RoutedEventArgs e)
        {
            Result = new OnlineConsentResult { Decision = OnlineConsentDecision.Denied };
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Show the consent dialog and return the result.
        /// </summary>
        public static OnlineConsentResult ShowConsent(Window? owner = null)
        {
            var dialog = new OnlineConsentDialog();
            if (owner != null)
            {
                dialog.Owner = owner;
            }
            dialog.ShowDialog();
            return dialog.Result;
        }
    }
}
