using System.Diagnostics;

namespace AlignmentCalc
{
    internal class Program
    {
        // Speed constants
        const double SPEED_0_5 = 251.159;
        const double SPEED_1 = 311.58;
        const double SPEED_2 = 387.421;
        const double SPEED_3 = 468.001;
        const double SPEED_4 = 576.0;

        // Display sampling limit
        const int MAX_RESULTS = 100;

        // Hard cap for full alignments stored in memory
        const int MAX_FULL_ALIGNMENTS = 1_000_000;

        static void Main()
        {
            double desiredX = PromptDouble("Enter the exact desired xpos: ", min: 0);

            // TPS must not be zero
            double tps = PromptDouble("Enter the TPS: ", min: 0.0000001);

            double speed = PromptSpeed();

            // Leniency allows blank input → 0
            double leniency = PromptDoubleAllowBlank("Enter positional leniency (leave blank for 0): ", min: 0);

            double unitsPerTick = speed / tps;

            if (unitsPerTick == 0)
            {
                Console.WriteLine("Error: unitsPerTick is zero. Check TPS and speed values.");
                return;
            }

            int maxTicks = (int)Math.Ceiling(desiredX / unitsPerTick);
            if (maxTicks <= 0)
            {
                maxTicks = 1;
            }

            Console.WriteLine("\nPlease wait, calculating alignments...");
            Stopwatch sw = Stopwatch.StartNew();

            var alignments = new List<(int ticksSincePortal, double portalMin, double portalMax)>();
            bool capped = false;

            int lastPrintedProgress = -1;

            for (int ticks = 0; ticks <= maxTicks; ticks++)
            {
                double centerPortalX = desiredX - ticks * unitsPerTick;
                double portalMin = centerPortalX - leniency;
                double portalMax = centerPortalX + leniency;

                if (portalMin < 0)
                {
                    continue;
                }

                if (alignments.Count < MAX_FULL_ALIGNMENTS)
                {
                    alignments.Add((ticks, portalMin, portalMax));
                }
                else
                {
                    capped = true;
                    break;
                }

                int progressPercent = (int)((double)ticks / maxTicks * 100);
                if (progressPercent != lastPrintedProgress)
                {
                    DrawProgressBarWithETA(ticks, maxTicks, sw);
                    lastPrintedProgress = progressPercent;
                }
            }

            DrawProgressBarWithETA(maxTicks, maxTicks, sw);
            sw.Stop();
            Console.WriteLine($" Done in {sw.Elapsed.TotalSeconds:F2} sec.\n");

            if (capped)
            {
                Console.WriteLine("The alignments list reached the 1,000,000‑item cap and was stopped early.");
                Console.WriteLine("(This prevents excessive memory usage.)\n");
            }

            // Build limited list for display
            List<(int, double, double)> limitedList;
            bool wasLimited = false;

            if (alignments.Count <= MAX_RESULTS)
            {
                limitedList = new List<(int, double, double)>(alignments);
            }
            else
            {
                wasLimited = true;

                limitedList = new List<(int, double, double)>
                {
                    alignments[0],
                    alignments[alignments.Count - 1]
                };

                int samples = MAX_RESULTS - limitedList.Count;
                double step = (double)alignments.Count / samples;

                for (int i = 1; i < samples; i++)
                {
                    int index = (int)(i * step);
                    index = Math.Clamp(index, 0, alignments.Count - 1);
                    limitedList.Add(alignments[index]);
                }

                limitedList = limitedList
                    .Distinct()
                    .OrderBy(a => a.Item1)
                    .ToList();
            }

            Console.WriteLine("\nAlignments:");
            PrintAlignments(limitedList, leniency);

            if (wasLimited || capped)
            {
                Console.WriteLine($"\nNote: This list does not contain all alignments. ({alignments.Count})");
                Console.Write("Show full list, export to CSV, or skip? (f/c/n): ");
                string input = Console.ReadLine()?.Trim().ToLower() ?? "n";

                if (input == "f")
                {
                    Console.WriteLine("\nFull list of alignments:");
                    PrintAlignments(alignments, leniency);
                }
                else if (input == "c")
                {
                    ExportCSV(alignments, leniency);
                }
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        static double PromptDouble(string prompt, double min = double.NegativeInfinity)
        {
            double val;
            Console.Write(prompt);
            while (!double.TryParse(Console.ReadLine(), out val) || val < min)
            {
                Console.Write($"Invalid input. {prompt}");
            }

            return val;
        }

        // Allows blank input → returns 0
        static double PromptDoubleAllowBlank(string prompt, double min = double.NegativeInfinity)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine()?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(input))
                {
                    return 0;
                }

                if (double.TryParse(input, out double val) && val >= min)
                {
                    return val;
                }

                Console.WriteLine("Invalid input. Leave blank for 0, or enter a valid number.");
            }
        }

        static double PromptSpeed()
        {
            while (true)
            {
                Console.Write("Enter the last speed portal hit (0.5x, 1x, 2x, 3x, or 4x): ");
                string input = Console.ReadLine()?.Trim().ToLower().Replace("x", "");
                switch (input)
                {
                    case "0.5": return SPEED_0_5;
                    case "1": return SPEED_1;
                    case "2": return SPEED_2;
                    case "3": return SPEED_3;
                    case "4": return SPEED_4;
                    default: Console.WriteLine("Invalid input."); break;
                }
            }
        }

        static void DrawProgressBarWithETA(int currentTick, int maxTicks, Stopwatch sw)
        {
            const int totalBlocks = 40;
            int filled = (int)((double)currentTick / Math.Max(maxTicks, 1) * totalBlocks);

            double elapsedSeconds = sw.Elapsed.TotalSeconds;
            double progressFraction = maxTicks == 0 ? 1 : (double)currentTick / maxTicks;
            double estimatedTotal = elapsedSeconds / Math.Max(progressFraction, 0.00001);
            double etaSeconds = estimatedTotal - elapsedSeconds;

            int etaMin = (int)(etaSeconds / 60);
            int etaSec = (int)(etaSeconds % 60);

            Console.Write("\r[");
            Console.Write(new string('█', filled));
            Console.Write(new string(' ', totalBlocks - filled));
            Console.Write($"] {progressFraction * 100,3:F0}% ETA: {etaMin:D2}:{etaSec:D2}");
        }

        static void PrintAlignments(List<(int ticksSincePortal, double portalMin, double portalMax)> list, double leniency)
        {
            bool collapse = leniency == 0;

            if (collapse)
            {
                Console.WriteLine(
                    $"{"Ticks since portal hit",-24} | {"portalX",-24}"
                );
                Console.WriteLine(new string('-', 55));

                foreach (var a in list)
                {
                    Console.WriteLine(
                        $"{a.ticksSincePortal,24} | {a.portalMin,24:F9}"
                    );
                    Console.WriteLine(new string('-', 55));
                }
            }
            else
            {
                Console.WriteLine(
                    $"{"Ticks since portal hit",-24} | {"portalX_min",-24} | {"portalX_max",-24}"
                );
                Console.WriteLine(new string('-', 80));

                foreach (var a in list)
                {
                    Console.WriteLine(
                        $"{a.ticksSincePortal,24} | " +
                        $"{a.portalMin,24:F9} | " +
                        $"{a.portalMax,24:F9}"
                    );
                    Console.WriteLine(new string('-', 80));
                }
            }
        }

        static void ExportCSV(List<(int ticksSincePortal, double portalMin, double portalMax)> list, double leniency)
        {
            bool collapse = leniency == 0;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string fileName = $"alignments_{timestamp}.csv";

            using (var writer = new StreamWriter(fileName))
            {
                if (collapse)
                {
                    writer.WriteLine("ticks_since_portal,portalX");
                    foreach (var a in list)
                    {
                        writer.WriteLine($"{a.ticksSincePortal},{a.portalMin:F9}");
                    }
                }
                else
                {
                    writer.WriteLine("ticks_since_portal,portalX_min,portalX_max");
                    foreach (var a in list)
                    {
                        writer.WriteLine($"{a.ticksSincePortal},{a.portalMin:F9},{a.portalMax:F9}");
                    }
                }
            }

            Console.WriteLine($"\nCSV exported successfully to: {Path.GetFullPath(fileName)}");
        }
    }
}
