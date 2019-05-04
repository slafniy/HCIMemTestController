using Microsoft.VisualBasic.Devices;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HCIMemTestController
{
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

         Parallel.For(0, threadCount, i =>
         {
            try
            {
               var memtestProcess = new MemTestProcessWrapper(MEMTEST_EXE, ram);
               _memtestProcessWrappers.Add(memtestProcess);
               memtestProcess.Start();
            }
            catch (Exception)
            {
               Console.Error.WriteLine("Exception during memtest start");
                  //                    CloseAllMemtests();
                  throw;
            }
         });
         _startTime = DateTime.UtcNow;
      }

      public bool AreAllMemtestsAlive()
      {
         foreach (var w in _memtestProcessWrappers)
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