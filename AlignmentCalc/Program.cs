using System.Diagnostics;

namespace AlignmentCalc
{
    internal class Program
    {
        // Speed constants
        const float SPEED_0_5 = 251.16008f;
        const float SPEED_1 = 311.5801f;
        const float SPEED_2 = 387.42014f;
        const float SPEED_3 = 468.00015f;
        const float SPEED_4 = 576.0002f;

        const int MAX_RESULTS = 1000;
        const int MAX_TICKS = 150_000;

        static void Main()
        {
            // Step 1: Xpos prompt
            var (xMin, xMax, originalInput, actualFloat) = PromptDisplayedX();

            Console.WriteLine($"\nYour input: {originalInput}");
            Console.WriteLine($"Rounded to float: {actualFloat:F10}");
            Console.WriteLine($"Float range: [{xMin:F10}, {xMax:F10}]");
            Console.WriteLine("\nThe 'float range' represents all real numbers that round to this float.\n");

            // Step 2: TPS + leniency prompts
            float tps = PromptFloat("Enter the TPS: ", min: 0.000001f);
            float leniency = PromptFloatAllowBlank("Enter positional leniency (leave blank for 0): ", min: 0);

            // Step 3: Speed prompt
            float speed = PromptSpeed();

            // Step 4: dx as GD computes it (float)
            float dx = speed / tps;
            if (dx == 0f)
            {
                Console.WriteLine("TPS is too high — per‑tick movement rounds to zero.");
                return;
            }

            // Step 5: Calculate maxTicks with 1M limit
            long maxTicksLong = (long)Math.Ceiling((xMax + leniency) / dx);

            if (maxTicksLong > MAX_TICKS)
            {
                Console.WriteLine($"Note: Calculation would require {maxTicksLong:N0} ticks.");
                Console.WriteLine($"Limiting to {MAX_TICKS:N0} ticks for memory and performance.");
                maxTicksLong = MAX_TICKS;
            }

            int maxTicks = (int)maxTicksLong;

            // Step 6: Pre-compute backward positions
            Console.WriteLine("\nPre-computing backward positions...");
            Stopwatch swCache = Stopwatch.StartNew();

            float[] backwardCache = new float[maxTicks + 1];
            backwardCache[0] = actualFloat;

            for (int i = 1; i <= maxTicks; i++)
            {
                backwardCache[i] = StepBackward(backwardCache[i - 1], dx);
            }

            swCache.Stop();
            Console.WriteLine($"Cache built in {swCache.Elapsed.TotalMilliseconds:F2} ms\n");

            // Step 7: Alignment loop (full forward simulation per candidate)
            Console.WriteLine("Calculating alignments...");
            Stopwatch sw = Stopwatch.StartNew();

            var alignments = new List<(int ticksSincePortal, float portalMin, float portalMax)>();
            int targetBits = BitConverter.SingleToInt32Bits(actualFloat);

            for (int ticks = 0; ticks <= maxTicks; ticks++)
            {
                float backwardPos = backwardCache[ticks];

                // Early exit: once backwardPos < -leniency, no future tick can produce valid portals
                if (backwardPos < -leniency)
                {
                    break;
                }

                // Generate up to 3 candidate portal positions
                int bits = BitConverter.SingleToInt32Bits(backwardPos);
                float[] candidates = new float[3];
                int count = 0;

                // prev neighbor
                if (bits > 0)
                {
                    float prev = BitConverter.Int32BitsToSingle(bits - 1);
                    if (prev >= 0f)
                    {
                        candidates[count++] = prev;
                    }
                }

                // exact
                if (backwardPos >= 0f)
                {
                    candidates[count++] = backwardPos;
                }

                // next neighbor
                float next = BitConverter.Int32BitsToSingle(bits + 1);
                if (next >= 0f)
                {
                    candidates[count++] = next;
                }

                float? bestPortal = null;
                float bestDistance = float.MaxValue;

                // Full forward simulation for each candidate
                for (int i = 0; i < count; i++)
                {
                    float cand = candidates[i];
                    float pos = cand;

                    // simulate forward ticks times
                    for (int t = 0; t < ticks; t++)
                    {
                        pos = StepForward(pos, dx);
                    }

                    if (BitConverter.SingleToInt32Bits(pos) == targetBits)
                    {
                        float dist = Math.Abs(cand);
                        if (dist < bestDistance)
                        {
                            bestDistance = dist;
                            bestPortal = cand;
                        }
                    }
                }

                // Record alignment
                if (bestPortal.HasValue)
                {
                    float portalMin = MathF.Max(0f, bestPortal.Value - leniency);
                    float portalMax = bestPortal.Value + leniency;
                    alignments.Add((ticks, portalMin, portalMax));
                }

                if (ticks % 1000 == 0 && ticks > 0)
                {
                    double elapsed = sw.Elapsed.TotalSeconds;
                    Console.Write($"\rTicks checked: {ticks}  Elapsed: {elapsed:F2}s");
                }
            }

            sw.Stop();
            Console.WriteLine($"\rDone in {sw.Elapsed.TotalSeconds:F2} sec (cache: {swCache.Elapsed.TotalSeconds:F2}s, search: {sw.Elapsed.TotalSeconds:F2}s)\n");

            // Step 8: Limit for display
            List<(int, float, float)> limitedList;
            bool wasLimited = false;

            if (alignments.Count <= MAX_RESULTS)
            {
                limitedList = new List<(int, float, float)>(alignments);
            }
            else
            {
                wasLimited = true;

                limitedList = new List<(int, float, float)>
                {
                    alignments[0],
                    alignments[^1]
                };

                int samples = MAX_RESULTS - limitedList.Count;
                float step = (float)alignments.Count / samples;

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
            PrintAlignments(limitedList);

            if (alignments.Count > 0)
            {
                if (wasLimited)
                {
                    Console.WriteLine($"\nNote: This list does not contain all {alignments.Count:N0} alignments.");
                }

                Console.Write("\nExport to CSV? (y/n): ");
                string input = Console.ReadLine()?.Trim().ToLower() ?? "n";

                if (input == "y")
                {
                    ExportCSV(alignments);
                }
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        // ==================== GD PHYSICS ====================

        static float StepForward(float pos, float dx) => (float)(pos + dx);
        static float StepBackward(float pos, float dx) => (float)(pos - dx);

        // ==================== INPUT / OUTPUT ====================

        static (float, float, string, float) PromptDisplayedX()
        {
            while (true)
            {
                Console.Write("Enter desired xpos (as shown in-game, 6 decimals): ");
                string input = Console.ReadLine()?.Trim() ?? "";

                if (float.TryParse(input, out float displayedX) && displayedX >= 0)
                {
                    float actualFloat = displayedX;
                    int bits = BitConverter.SingleToInt32Bits(actualFloat);

                    float prev = bits > 0 ? BitConverter.Int32BitsToSingle(bits - 1) : 0f;
                    float next = BitConverter.Int32BitsToSingle(bits + 1);

                    float xMin = MathF.Max(0, prev);
                    float xMax = next;

                    return (xMin, xMax, input, actualFloat);
                }

                Console.WriteLine("Invalid input. Enter a number with up to 6 decimals.");
            }
        }

        static float PromptFloat(string prompt, float min = float.NegativeInfinity)
        {
            float val;
            Console.Write(prompt);
            while (!float.TryParse(Console.ReadLine(), out val) || val < min)
            {
                Console.Write($"Invalid input. {prompt}");
            }

            return val;
        }

        static float PromptFloatAllowBlank(string prompt, float min = float.NegativeInfinity)
        {
            while (true)
            {
                Console.Write(prompt);
                string input = Console.ReadLine()?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(input))
                {
                    return 0;
                }

                if (float.TryParse(input, out float val) && val >= min)
                {
                    return val;
                }

                Console.WriteLine("Invalid input. Leave blank for 0, or enter a valid number.");
            }
        }

        static float PromptSpeed()
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

        static void PrintAlignments(List<(int ticksSincePortal, float portalMin, float portalMax)> list)
        {
            Console.WriteLine($"{"Ticks since portal hit",-24} | {"portalX_min",-24} | {"portalX_max",-24}");
            Console.WriteLine(new string('-', 80));
            foreach (var a in list)
            {
                Console.WriteLine($"{a.ticksSincePortal,24} | {a.portalMin,24:F6} | {a.portalMax,24:F6}");
            }
        }

        static void ExportCSV(List<(int ticksSincePortal, float portalMin, float portalMax)> list)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string fileName = $"alignments_{timestamp}.csv";
            using var writer = new StreamWriter(fileName);
            writer.WriteLine("ticks_since_portal,portalX_min,portalX_max");
            foreach (var a in list)
            {
                writer.WriteLine($"{a.ticksSincePortal},{a.portalMin:F6},{a.portalMax:F6}");
            }

            Console.WriteLine($"\nCSV exported successfully to: {Path.GetFullPath(fileName)}");
        }
    }
}
