using System;
using System.Runtime.InteropServices;

namespace Meshtastic.ConsoleHost
{
    internal static class TaskbarIconRefresh
    {
        [ComImport, Guid("56FDF342-FD6D-11D0-958A-006097C9A090")]
        private class TaskbarList { }

        [ComImport, Guid("56FDF344-FD6D-11D0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList
        {
            void HrInit();
            void AddTab(IntPtr window);
            void DeleteTab(IntPtr window);
            void ActivateTab(IntPtr window);
            void SetActiveAlt(IntPtr window);
        }

        // Re-register the taskbar button without recreating the terminal window.
        public static void Refresh(IntPtr window, bool active)
        {
            ITaskbarList taskbar = null;
            try
            {
                taskbar = (ITaskbarList)new TaskbarList();
                taskbar.HrInit();
                try { taskbar.DeleteTab(window); }
                finally { taskbar.AddTab(window); }
                if (active) taskbar.ActivateTab(window);
            }
            catch (COMException) { }
            finally { if (taskbar != null) Marshal.ReleaseComObject(taskbar); }
        }
    }
}
