namespace Hollow.HoUnityTools.WarudoModUtils
{
    /// <summary>
    /// Runtime module rendered by HoWarudoRuntimeHub.
    /// Implementations provide content only; the Hub owns the window and layout.
    /// </summary>
    public interface IHoWarudoRuntimeModule
    {
        string Id { get; }
        string DisplayName { get; }
        int Order { get; }

        void DrawRuntimeGUI(HoWarudoRuntimeGUIContext context);
    }
}
