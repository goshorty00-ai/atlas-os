using System.Threading.Tasks;

namespace AtlasAI.Services
{
    /// <summary>
    /// Atlas-themed dialog service for displaying messages to the user.
    /// Replaces System.Windows.MessageBox with a futuristic, themed experience.
    /// </summary>
    public interface IAtlasDialogService
    {
        /// <summary>
        /// Show an async dialog and wait for user response.
        /// Thread-safe - automatically marshals to UI thread.
        /// </summary>
        /// <param name="title">Dialog title</param>
        /// <param name="message">Dialog message</param>
        /// <param name="buttons">Button configuration (OK, OKCancel, YesNo, etc.)</param>
        /// <param name="icon">Icon type (None, Info, Warning, Error, Question)</param>
        /// <param name="defaultButton">Which button should be focused by default</param>
        /// <param name="showDontShowAgain">Show "Don't show this again" checkbox</param>
        /// <param name="dontShowAgainKey">Key for storing "don't show again" preference</param>
        /// <returns>Dialog result</returns>
        Task<AtlasDialogResult> ShowAsync(
            string title,
            string message,
            AtlasDialogButtons buttons = AtlasDialogButtons.OK,
            AtlasDialogIcon icon = AtlasDialogIcon.None,
            AtlasDialogButton defaultButton = AtlasDialogButton.Button1,
            bool showDontShowAgain = false,
            string? dontShowAgainKey = null);

        /// <summary>
        /// Show an information dialog (shortcut method)
        /// </summary>
        Task ShowInfoAsync(string title, string message);

        /// <summary>
        /// Show a warning dialog (shortcut method)
        /// </summary>
        Task ShowWarningAsync(string title, string message);

        /// <summary>
        /// Show an error dialog (shortcut method)
        /// </summary>
        Task ShowErrorAsync(string title, string message);

        /// <summary>
        /// Show a confirmation dialog (Yes/No)
        /// </summary>
        Task<bool> ShowConfirmAsync(string title, string message, AtlasDialogIcon icon = AtlasDialogIcon.Question);

        /// <summary>
        /// Check if user has opted to not show a dialog again
        /// </summary>
        bool ShouldShowDialog(string key);

        /// <summary>
        /// Clear "don't show again" preference for a specific key
        /// </summary>
        void ClearDontShowAgain(string key);

        /// <summary>
        /// Clear all "don't show again" preferences
        /// </summary>
        void ClearAllDontShowAgain();
    }

    /// <summary>
    /// Button configurations for dialogs
    /// </summary>
    public enum AtlasDialogButtons
    {
        OK,
        OKCancel,
        YesNo,
        YesNoCancel,
        RetryCancel,
        AbortRetryIgnore
    }

    /// <summary>
    /// Icon types for dialogs
    /// </summary>
    public enum AtlasDialogIcon
    {
        None,
        Info,
        Warning,
        Error,
        Question,
        Success
    }

    /// <summary>
    /// Which button should be default
    /// </summary>
    public enum AtlasDialogButton
    {
        Button1,
        Button2,
        Button3
    }

    /// <summary>
    /// Result from dialog
    /// </summary>
    public enum AtlasDialogResult
    {
        None = 0,
        OK = 1,
        Cancel = 2,
        Yes = 6,
        No = 7,
        Abort = 3,
        Retry = 4,
        Ignore = 5
    }
}
