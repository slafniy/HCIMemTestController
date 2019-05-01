using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HCIMemTestController
{
   public struct Window
   {
      public IntPtr Handle;
      public string Title;
   }

   public class MemTestProcessWrapper
   {
      private const string MEMTEST_EDT_RAM = "Edit1";
      private const string MEMTEST_BTN_START = "Button1";
      private const string MEMTEST_STATIC_COVERAGE = "Static1";
      private const int BM_CLICK = 0xF5;
      private const int WM_SETTEXT = 0xC;
      private const int SW_MINIMIZE = 6;
      private const int WM_CLOSE = 0x0010; // Message to close window by handle

      private readonly string _pathToMemTestExe;
      private readonly double _ramSizeMb;
      private Process _process;

      public MemTestProcessWrapper(string pathToMemTestExe, double ramSizeMb)
      {
         _pathToMemTestExe = pathToMemTestExe;
         _ramSizeMb = ramSizeMb;
         if (!File.Exists(_pathToMemTestExe))
         {
            throw new Exception($"Memtest file doesn't exists in {_pathToMemTestExe}");
         }
      }

      public int PID => _process.Id;

      public void Start()
      {
         var startInfo = new ProcessStartInfo
         {
            FileName = _pathToMemTestExe,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
         };
         _process = new Process { StartInfo = startInfo };
         _process.Start();

         CloseAnnoyingWindows("Welcome, New MemTest User"); // in freeware version
         WaitMainWindowHandle();
         User32Helper.ShowWindow(_process.MainWindowHandle, User32Helper.SW_HIDE);
         ControlSetText(_process.MainWindowHandle, MEMTEST_EDT_RAM, $"{_ramSizeMb:f2}");
         ControlClick(_process.MainWindowHandle, MEMTEST_BTN_START);
         CloseAnnoyingWindows("Message for first-time users");
      }

      private static void ControlClick(IntPtr hwndParent, string className)
      {
         IntPtr hwnd = FindUIElementByName(hwndParent, className);
         if (hwnd == IntPtr.Zero)
         {
            return;
         }

         User32Helper.SendNotifyMessage(hwnd, BM_CLICK, IntPtr.Zero, null);
      }

      public void Close()
      {
         _process.Kill();
      }

      public bool IsAlive()
      {
         return !_process.HasExited;
      }

      private void WaitMainWindowHandle(uint timeoutMs = 10000)
      {
         DateTime end = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
         while (DateTime.UtcNow < end)
         {
            if (_process.MainWindowHandle != IntPtr.Zero)
            {
               return;
            }

            Thread.Sleep(5);
         }

         throw new Exception($"Main window handle is null after {timeoutMs} ms!");
      }

      private static void ControlSetText(IntPtr hwndParent, string className, string text)
      {
         IntPtr hwnd = FindUIElementByName(hwndParent, className);
         if (hwnd == IntPtr.Zero)
         {
            throw new Exception("Handle is null!");
         }

         User32Helper.SendMessage(hwnd, WM_SETTEXT, IntPtr.Zero, text);
      }

      private static Tuple<string, int> SplitClassName(string className)
      {
         // class_name should be <classname><n>
         // tries to split class_name as above
         // returns (<classname>, <n>) if possible
         // otherwise, returns null

         var regex = new Regex(@"([a-zA-Z]+)(\d+)");
         Match match = regex.Match(className);

         if (!match.Success)
         {
            return null;
         }

         return Tuple.Create(
             match.Groups[1].Value,
             Convert.ToInt32(match.Groups[2].Value)
         );
      }

      private static IntPtr FindUIElementByName(IntPtr hwndParent, string className)
      {
         //  class_name should be <classname><n>
         //  where <classname> is the name of the class to find
         //        <n>         is the nth window with that matches <classname> (1 indexed)
         //  e.g. Edit1
         //  returns the handle to the window if found
         //  otherwise, returns IntPtr.Zero

         if (hwndParent == IntPtr.Zero)
         {
            throw new Exception("Parent handle is null!");
         }

         Tuple<string, int> name = SplitClassName(className);
         if (name == null)
         {
            throw new Exception("Name is null!");
         }

         IntPtr hwnd = IntPtr.Zero;
         for (int i = 0; i < name.Item2; i++)
         {
            hwnd = User32Helper.FindWindowEx(hwndParent, hwnd, name.Item1, null);
         }

         return hwnd;
      }

      private void CloseAnnoyingWindows(string title, uint timeToCloseMs = 5000, IntPtr parent=default(IntPtr))
      {
         DateTime end = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeToCloseMs);
         while (DateTime.UtcNow < end)
         {
            IEnumerable<Window> windows = GetWindowsTitles(parent);
            foreach (var w in windows)
            {
               if (w.Title == title)
               {
                  User32Helper.ShowWindow(w.Handle, User32Helper.SW_HIDE);
                  Console.WriteLine($"Found annoying window {w.Title}, sending close");
                  User32Helper.SendMessage(w.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                  return;
               }

               Thread.Sleep(5);
            }
         }
      }

      private IEnumerable<Window> GetWindowsTitles(IntPtr parent=default(IntPtr))
      {
         var memTestWindowTitles = new List<Window>();

         IEnumerable<IntPtr> allWindows = User32Helper.GetChildWindows(parent);
         foreach (IntPtr handle in allWindows)
         {
            User32Helper.GetWindowThreadProcessId(handle, out uint pid);
            if (pid != _process.Id)
            {
               continue;
            }
            int textLength = User32Helper.GetWindowTextLength(handle);
            var title = new StringBuilder(textLength + 1);
            User32Helper.GetWindowText(handle, title, title.Capacity);
            memTestWindowTitles.Add(new Window { Handle = handle, Title = title.ToString() });
         }

         return memTestWindowTitles;
      }

      private static string ControlGetText(IntPtr hwnd, string className)
      {
         IntPtr hwndControl = FindUIElementByName(hwnd, className);
         if (hwndControl == IntPtr.Zero) return null;
         int len = User32Helper.GetWindowTextLength(hwndControl);
         var str = new StringBuilder(len + 1);
         User32Helper.GetWindowText(hwndControl, str, str.Capacity);
         return str.ToString();
      }

      public Tuple<double, int> GetCoverageInfo()
      {
         string str = ControlGetText(_process.MainWindowHandle, MEMTEST_STATIC_COVERAGE);
         if (string.IsNullOrEmpty(str) || !str.Contains("Coverage"))
         {
            return null;
         }

         // Test over. 47.3% Coverage, 0 Errors
         //            ^^^^^^^^^^^^^^^^^^^^^^^^
         int start = str.IndexOfAny("0123456789".ToCharArray());
         if (start == -1) return null;
         str = str.Substring(start);

         // 47.3% Coverage, 0 Errors
         // ^^^^
         // some countries use a comma as the decimal point
         string coverageStr = str.Split("%".ToCharArray())[0].Replace(',', '.');
         double.TryParse(coverageStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double coverage);

         // 47.3% Coverage, 0 Errors
         //                 ^^^^^^^^
         start = str.IndexOf("Coverage, ", StringComparison.Ordinal) + "Coverage, ".Length;
         str = str.Substring(start);
         // 0 Errors
         // ^
         int errors = Convert.ToInt32(str.Substring(0, str.IndexOf(" Errors", StringComparison.Ordinal)));

         return Tuple.Create(coverage, errors);
      }
   }
}