using Microsoft.VisualBasic.Devices;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HCIMemTestController
{
   public struct Window
   {
      public IntPtr Handle;
      public string Title;
   }
   
   public class Coverage
   {
      public int ErrorCount;
      public double CoveragePercent;
      public int ProcessID;

      public Coverage(int errorCount, double coveragePercent, int processID)
      {
         ErrorCount = errorCount;
         CoveragePercent = coveragePercent;
         ProcessID = processID;
      }
   }

   public class Controller
   {
      private const int WM_CLOSE = 0x0010; // Message to close window by handle
      private const string MEMTEST_EXE = "memtest.exe";
      private const int MEMTEST_MAX_RAM = 2048;
      private DateTime _startTime;

      private ConcurrentBag<MemTestProcessWrapper> _memtestProcessWrappers = new ConcurrentBag<MemTestProcessWrapper>();

      public Controller()
      {
         if (File.Exists(MEMTEST_EXE))
         {
            return;
         }
         Console.Error.WriteLine($"HCI memtest util wasn't found in {Path.GetFullPath(MEMTEST_EXE)}");
         Environment.Exit(-1);
      }

      public static ulong GetFreeRam()
      {
         return new ComputerInfo().AvailablePhysicalMemory / (1024 * 1024);
      }

      public Tuple<List<Coverage>, TimeSpan> GetCoverages()
      {
         var result = new ConcurrentBag<Coverage>();
         Parallel.ForEach(_memtestProcessWrappers, memtestProcess =>
         {
            Tuple<double, int> cov = memtestProcess.GetCoverageInfo() ?? new Tuple<double, int>(0, 0);
            result.Add(new Coverage(cov.Item2, cov.Item1, memtestProcess.PID));
         });

         List<Coverage> listResult = result.ToList();
         listResult.Sort((item, other) => item.ProcessID.CompareTo(other.ProcessID));
         return new Tuple<List<Coverage>, TimeSpan>(listResult, DateTime.UtcNow - _startTime);
      }

      public void StartMemtests(int threadCount, double ramCount)
      {
         CloseAllMemtests();

         double ram = ramCount / threadCount;

         if (ram > MEMTEST_MAX_RAM)
         {
            throw new Exception($"You requested {threadCount} threads and {ramCount} mb RAM," +
                                $"which gives {ram} per process, that exceeds freeware HCI " +
                                $"memtest limit - {MEMTEST_MAX_RAM}");
         }
         
         // Just run memtest processes
         Parallel.For(0, threadCount, i =>
         {
            try
            {
               var memtestProcess = new MemTestProcessWrapper(MEMTEST_EXE, ram);
               _memtestProcessWrappers.Add(memtestProcess);
               memtestProcess.InitialStart();
            }
            catch (Exception)
            {
               Console.Error.WriteLine("Exception during memtest start");
                  //                    CloseAllMemtests();
                  throw;
            }
         });
         
         CloseAnnoyingWindows("Welcome, New MemTest User"); // in freeware version
         
         // Set RAM and start
         Parallel.ForEach(_memtestProcessWrappers, memtestProcess =>
         {
            memtestProcess.MemtestPrepareAndStart();
         });

         _startTime = DateTime.UtcNow;
         CloseAnnoyingWindows("Message for first-time users");
      }

      private static void CloseAnnoyingWindows(string title, uint timeToCloseMs = 5000, IntPtr parent=default(IntPtr))
      {
         DateTime end = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeToCloseMs);
         while (DateTime.UtcNow < end)
         {
            IEnumerable<Window> windows = GetWindowsTitles(parent);
            foreach (Window w in windows)
            {
               if (w.Title != title)
               {
                  continue;
               }
               User32Helper.ShowWindow(w.Handle, User32Helper.SW_HIDE);
               Console.WriteLine($"Found annoying window {w.Title}, sending close");
               User32Helper.SendMessage(w.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
         }
      }
      
      private static IEnumerable<Window> GetWindowsTitles(IntPtr parent=default(IntPtr))
      {
         var memTestWindowTitles = new List<Window>();

         IEnumerable<IntPtr> allWindows = User32Helper.GetChildWindows(parent);
         foreach (IntPtr handle in allWindows)
         {
            int textLength = User32Helper.GetWindowTextLength(handle);
            var title = new StringBuilder(textLength + 1);
            User32Helper.GetWindowText(handle, title, title.Capacity);
            memTestWindowTitles.Add(new Window { Handle = handle, Title = title.ToString() });
         }
         return memTestWindowTitles;
      }
      
      public bool AreAllMemtestsAlive()
      {
         foreach (MemTestProcessWrapper w in _memtestProcessWrappers)
         {
            if (!w.IsAlive())
            {
               return false;
            }
         }
         return true;
      }

      public void CloseAllMemtests()
      {
         // empty current process wrappers
         _memtestProcessWrappers = new ConcurrentBag<MemTestProcessWrapper>();
         // close all MemTests, regardless of if they were started by MemTestHelper
         string name = MEMTEST_EXE.Substring(0, MEMTEST_EXE.Length - 4); // remove the .exe
         Process[] processes = Process.GetProcessesByName(name);
         Parallel.ForEach(processes, p => { p.Kill(); });
      }
   }
}