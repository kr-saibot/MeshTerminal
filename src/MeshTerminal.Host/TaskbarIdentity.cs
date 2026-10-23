using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace MeshTerminal.Host
{
    internal static class TaskbarIdentity
    {
        public const string DisplayName = "MeshTerminal";
        internal static string GetApplicationId(string executable)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return "MeshTerminal.Host." + BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(executable).ToUpperInvariant()))).Replace("-", "");
        }
        private static readonly Guid PropertyFormat = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey { public Guid Format; public uint Id; }
        // PROPVARIANT includes an eight-byte header and a union large enough
        // for a pointer/count pair on both x86 and x64.
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant { public ushort Type, Reserved1, Reserved2, Reserved3; public IntPtr Value, Padding; }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }
        [DllImport("shell32.dll", PreserveSig = false)]
        private static extern void SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, out IPropertyStore store);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string message);
        public static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarButtonCreated");

        private static void SetString(IPropertyStore store, uint id, string text)
        {
            var key = new PropertyKey { Format = PropertyFormat, Id = id };
            var value = new PropVariant { Type = 31, Value = Marshal.StringToCoTaskMemUni(text) };
            try { store.SetValue(ref key, ref value); }
            finally { Marshal.FreeCoTaskMem(value.Value); }
        }
        public static void Apply(IntPtr window, Icon icon, string iconId)
        {
            SendMessage(window, 0x80, IntPtr.Zero, icon.Handle);
            SendMessage(window, 0x80, new IntPtr(1), icon.Handle);
            IPropertyStore store = null;
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MeshTerminal.Host", "TaskbarIcons", GetApplicationId(Application.ExecutablePath));
                Directory.CreateDirectory(directory);
                // A stable, separate file for each selection avoids Explorer's
                // resource-path cache returning the previous icon.
                string path;
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    path = Path.Combine(directory, BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(iconId ?? "default"))).Replace("-", "") + ".ico");
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) icon.Save(stream);
                var iid = typeof(IPropertyStore).GUID;
                SHGetPropertyStoreForWindow(window, ref iid, out store);
                SetString(store, 2, "\"" + Application.ExecutablePath + "\"");
                SetString(store, 3, path + ",0");
                SetString(store, 4, DisplayName);
                // Set identity last: this notifies Explorer to re-read metadata.
                SetString(store, 5, GetApplicationId(Application.ExecutablePath));
                store.Commit();
            }
            catch (Exception exception) when (exception is COMException || exception is IOException || exception is UnauthorizedAccessException)
            { System.Diagnostics.Trace.WriteLine("Taskbar icon: " + exception.Message); }
            finally { if (store != null) Marshal.ReleaseComObject(store); }
        }
    }
}
