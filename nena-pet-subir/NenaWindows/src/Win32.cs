using System;
using System.Runtime.InteropServices;

namespace NenaPet
{
    // Llamadas nativas de Windows
    public static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
        // Idioma de entrada de la ventana activa (chino, japonés y coreano escriben con un editor IME)
        public static bool ForegroundIsImeLanguage()
        {
            try
            {
                uint pid; uint th = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                int lang = (int)((long)GetKeyboardLayout(th) & 0x3FF);
                return lang == 0x04 || lang == 0x11 || lang == 0x12;
            }
            catch (Exception) { return false; }
        }
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        public static int GetExStyle(IntPtr h)
        {
            return IntPtr.Size == 8 ? (int)GetWindowLongPtr64(h, GWL_EXSTYLE) : GetWindowLong32(h, GWL_EXSTYLE);
        }
        public static void SetExStyle(IntPtr h, int v)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, GWL_EXSTYLE, new IntPtr(v)); else SetWindowLong32(h, GWL_EXSTYLE, v);
        }
        // La ventana no roba el foco al hacer clic y no sale en Alt+Tab
        public static void MakeNoActivate(IntPtr h)
        {
            SetExStyle(h, GetExStyle(h) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
    }

    // Detecta que se ha pulsado una tecla o un botón del ratón consultando su estado (sin ganchos globales
    // ni registrar qué tecla es): solo cuenta pulsaciones nuevas para animar a Nena.
    public class InputPoller
    {
        bool[] prev = new bool[256];
        uint lastInput; bool lastInputInit; Native.POINT lastPt;
        public event Action<uint> KeyPressed;      // pulsación nueva (sin repeticiones de tecla mantenida)
        public event Action Activity;              // clic de ratón

        public void Poll()
        {
            anyKeyDown = false; countedThisPoll = false;
            for (int vk = 1; vk < 255; vk++)
            {
                if (vk == 0x0C || (vk >= 0x10 && vk <= 0x12 && false)) continue;
                bool d = (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
                if (d && !prev[vk])
                {
                    if (vk == 1 || vk == 2 || vk == 4) { if (Activity != null) Activity(); }
                    else { countedThisPoll = true; if (KeyPressed != null) KeyPressed((uint)vk); }
                }
                prev[vk] = d;
                if (d && vk > 4 && vk != 0x10 && vk != 0x11 && vk != 0x12) anyKeyDown = true;
            }
            // Teclados/editores de entrada (IME chino, japonés, coreano, teclado táctil...) que no dejan ver la tecla física:
            // se detecta cualquier entrada nueva del sistema que no sea ratón y no se haya contado ya.
            try
            {
                Native.LASTINPUTINFO li = new Native.LASTINPUTINFO(); li.cbSize = (uint)Marshal.SizeOf(typeof(Native.LASTINPUTINFO));
                Native.POINT pt; Native.GetCursorPos(out pt);
                if (Native.GetLastInputInfo(ref li))
                {
                    bool moved = pt.X != lastPt.X || pt.Y != lastPt.Y;
                    bool mouseDown = (Native.GetAsyncKeyState(1) & 0x8000) != 0 || (Native.GetAsyncKeyState(2) & 0x8000) != 0 || (Native.GetAsyncKeyState(4) & 0x8000) != 0;
                    if (lastInputInit && li.dwTime != lastInput && !moved && !mouseDown && !anyKeyDown && !countedThisPoll && Native.ForegroundIsImeLanguage())
                        if (KeyPressed != null) KeyPressed(0);
                    lastInput = li.dwTime; lastInputInit = true; lastPt = pt;
                }
            }
            catch (Exception) { }
        }
        bool anyKeyDown, countedThisPoll;
    }
}
