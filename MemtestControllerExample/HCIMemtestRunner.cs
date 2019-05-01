﻿using System;
using System.Collections.Generic;
using System.Threading;
using HCIMemTestController;

namespace RunHCIOfficialCLI
{
    public static class HCIMemtestRunner
    {
        public static void Main(string[] args)
        {
            var controller = new Controller();
            
            Console.WriteLine($"Available RAM: {Controller.GetFreeRam()}");
            
            controller.StartMemtests(4, 128);

            ReportProgress();

            controller.CloseAllMemtests();


            void ReportProgress(double stopOnCoverage = 15, uint intervalMs = 1000)
            {
                while (true)
                {
                    Tuple<List<Coverage>, TimeSpan> coveragesAndTime = controller.GetCoverages();
                    Console.WriteLine($"======= Elapsed time, sec: {coveragesAndTime.Item2.TotalSeconds:f1}  =======");
                    foreach (Coverage cov in coveragesAndTime.Item1)
                    {
                        if (cov == null)
                        {
                            continue;
                        }
                        Console.WriteLine($"Coverage: {cov.CoveragePercent}, errors: {cov.ErrorCount}");
                        if (cov.CoveragePercent >= stopOnCoverage)
                        {
                            return;
                        }
                    }
                    Thread.Sleep((int) intervalMs);
                }
            }
        }
    }
}