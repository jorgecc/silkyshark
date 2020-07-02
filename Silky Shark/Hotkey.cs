﻿using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Silky_Shark
{
    public class Hotkey : IMessageFilter, IDisposable
    {
        private bool disposed;
        private const int WM_HOTKEY = 0x0312;
        private IntPtr Handle { get; set; }
        private int ID { get; set; }
        private KeyModifiers Modifiers { get; set; }
        private Keys Key { get; set; }
        public event EventHandler HotKeyPressed;

        public Hotkey(IntPtr handle, int id, KeyModifiers modifiers, Keys key)
        {
            if (key == Keys.None || modifiers == KeyModifiers.None) throw new Exception();

            Handle = handle;
            ID = id;
            Modifiers = modifiers;
            Key = key;
            RegisterHotKey();
            Application.AddMessageFilter(this);
        }

        [Flags]
        public enum KeyModifiers
        {
            None = 0,
            Alt = 1,
            Ctrl = 2,
            Shift = 4
        }

        public static KeyModifiers GetModifiers(Keys keydata, out Keys key)
        {

            key = keydata;
            var modifers = KeyModifiers.None;

            if ((keydata & Keys.Control) > 0)
            {
                modifers |= KeyModifiers.Ctrl;

                key = keydata ^ Keys.Control;
            }

            if ((keydata & Keys.Shift) > 0)
            {
                modifers |= KeyModifiers.Shift;
                key ^= Keys.Shift;
            }

            if ((keydata & Keys.Alt) > 0)
            {
                modifers |= KeyModifiers.Alt;
                key ^= Keys.Alt;
            }
            
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
            {
                key = Keys.None;
            }

            return modifers;
        }

        private void RegisterHotKey()
        {
            var isKeyRegisterd = RegisterHotKey(Handle, ID, Modifiers, Key);

            if (!isKeyRegisterd)
            {
                UnregisterHotKey(IntPtr.Zero, ID);
                isKeyRegisterd = RegisterHotKey(Handle, ID, Modifiers, Key);

                if (!isKeyRegisterd) throw new Exception();
            }
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_HOTKEY
                && m.HWnd == Handle
                && m.WParam == (IntPtr)ID
                && HotKeyPressed != null)
            {
                HotKeyPressed(this, EventArgs.Empty);
                return true;
            }

            return false;
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            if (disposing)
            {
                Application.RemoveMessageFilter(this);
                UnregisterHotKey(Handle, ID);
            }

            disposed = true;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, KeyModifiers fsModifiers, Keys vk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
