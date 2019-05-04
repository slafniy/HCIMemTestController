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
   public class MemTestProcessWrapper
   {
      private const string MEMTEST_EDT_RAM = "Edit1";
      private const string MEMTEST_BTN_START = "Button1";
      private const string MEMTEST_STATIC_COVERAGE = "Static1";
      private const int BM_CLICK = 0xF5;
      private const int WM_SETTEXT = 0xC;


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

      public void InitialStart()
      {
         var startInfo = new ProcessStartInfo
         {
            FileName = _pathToMemTestExe,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false
         };
         _process = new Process { StartInfo = startInfo };
         _process.Start();

//         CloseAnnoyingWindows("Welcome, New MemTest User"); // in freeware version

//         CloseAnnoyingWindows("Message for first-time users");
      }

      public void MemtestPrepareAndStart()
      {
         WaitMainWindowHandle();  // Someone should close startup dialog! Waiting for it
         User32Helper.ShowWindow(_process.MainWindowHandle, User32Helper.SW_HIDE);
         ControlSetText(_process.MainWindowHandle, MEMTEST_EDT_RAM, $"{_ramSizeMb:f2}");
         ControlClick(_process.MainWindowHandle, MEMTEST_BTN_START);
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

      private void WaitMainWindowHandle(uint timeoutMs = 100000)
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
         int start = str.IndexOfAny("0123456789".ToCharArray());  // TODO: this looks NOT GOOD
         if (start == -1)
         {
            return null;
         }
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