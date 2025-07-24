using System.Runtime.InteropServices;


namespace ArtaleAI.GameWindow
{

    public static class SystemAPI
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}