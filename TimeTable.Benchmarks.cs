using System.Diagnostics;
using System.Text.Json;
using Chneau.OpenHours;
using Xunit;

namespace Chneau.TimeTable.Benchmarks;

public static class TimeTableBenchmarks
{
    public static void RunBenchmarks(TextWriter? writer = null)
    {
        writer ??= Console.Out;
        writer.WriteLine("========================================================");
        writer.WriteLine("Running TimeTable Benchmarks (.NET 10 Suite)");
        writer.WriteLine("========================================================");

        var oh = OpenHours.OpenHours.Parse("Mo-Fr 08:00-18:00");
        var baseTime = new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc);
        var oneHour = TimeSpan.FromHours(1);

        // Warm-up JIT Tier 0 / Tier 1 compilation
        var warmupTT = new TimeTable<double>(5.0, oh);
        for (int i = 0; i < 5000; i++)
        {
            var next = warmupTT.Add(baseTime.AddHours(i % 24), oneHour, 1.0);
            if (next != null) warmupTT = next;
            warmupTT.When(baseTime.AddHours(i % 10), oneHour, 1.0);
            if (warmupTT.Count > 50) warmupTT = new TimeTable<double>(5.0, oh);
        }

        // Benchmark 1: Sequential Add Operations
        int addOps = 50_000;
        var tt = new TimeTable<double>(100.0, oh);
        long memBefore1 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < addOps; i++)
        {
            var added = tt.Add(baseTime.AddMinutes(i * 10), oneHour, 1.0);
            if (added != null) tt = added;
            if (tt.Count > 200) tt = new TimeTable<double>(100.0, oh);
        }
        sw.Stop();
        long memAfter1 = GC.GetAllocatedBytesForCurrentThread();
        double d1 = sw.Elapsed.TotalMilliseconds;
        double us1 = sw.Elapsed.TotalMicroseconds / addOps;
        double b1 = (double)(memAfter1 - memBefore1) / addOps;
        writer.WriteLine($"1. Sequential Add ({addOps} calls):               {d1,6:F1} ms ({us1:F3} us/op, {b1:F1} B/op)");

        // Benchmark 2: When (Capacity Query) with OpenHours constraint
        int whenOps = 20_000;
        var searchTT = new TimeTable<double>(3.0, oh);
        for (int i = 0; i < 5; i++)
        {
            var added = searchTT.Add(baseTime.AddHours(i * 2), TimeSpan.FromHours(2), 2.0);
            if (added != null) searchTT = added;
        }

        long memBefore2 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        for (int i = 0; i < whenOps; i++)
        {
            searchTT.When(baseTime.AddHours(i % 40), oneHour, 2.0);
        }
        sw.Stop();
        long memAfter2 = GC.GetAllocatedBytesForCurrentThread();
        double d2 = sw.Elapsed.TotalMilliseconds;
        double us2 = sw.Elapsed.TotalMicroseconds / whenOps;
        double b2 = (double)(memAfter2 - memBefore2) / whenOps;
        writer.WriteLine($"2. Capacity Search When ({whenOps} calls):        {d2,6:F1} ms ({us2:F3} us/op, {b2:F1} B/op)");

        // Benchmark 3: System.Text.Json Deserialization
        int jsonOps = 10_000;
        var sample = new TimeTable<double>(10.0, oh);
        sample = sample.Add(baseTime, oneHour, 1.0)!;
        sample = sample.Add(baseTime.AddHours(2), oneHour, 2.0)!;
        string json = JsonSerializer.Serialize(sample);

        long memBefore3 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        for (int i = 0; i < jsonOps; i++)
        {
            JsonSerializer.Deserialize<TimeTable<double>>(json);
        }
        sw.Stop();
        long memAfter3 = GC.GetAllocatedBytesForCurrentThread();
        double d3 = sw.Elapsed.TotalMilliseconds;
        double us3 = sw.Elapsed.TotalMicroseconds / jsonOps;
        double b3 = (double)(memAfter3 - memBefore3) / jsonOps;
        writer.WriteLine($"3. JSON Deserialization ({jsonOps} calls):         {d3,6:F1} ms ({us3:F3} us/op, {b3:F1} B/op)");
        writer.WriteLine("========================================================");
    }
}

public class BenchmarkRunnerTests
{
    [Fact]
    public void TestRunBenchmarkSuite()
    {
        TimeTableBenchmarks.RunBenchmarks(Console.Out);
    }
}
