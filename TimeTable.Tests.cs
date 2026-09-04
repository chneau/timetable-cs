using System.Text.Json;
using Chneau.OpenHours;
using Xunit;

namespace Chneau.TimeTable.Tests;

public class TimeTableTests
{
    [Fact]
    public void TestEmptyAndDefaultConstructor()
    {
        var tt = new TimeTable<double>(5.0);
        Assert.Equal(5.0, tt.Max);
        Assert.Equal(0, tt.Count);
        Assert.NotNull(tt.Constraint);
        Assert.Empty(tt.Points);
    }

    [Fact]
    public void TestValidation()
    {
        var tt = new TimeTable<double>(5.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimeTable<double>(-1.0));
        Assert.Null(tt.Add(DateTime.Now, TimeSpan.Zero, 1.0));
        Assert.Null(tt.Add(DateTime.Now, TimeSpan.FromHours(-1), 1.0));
        Assert.Null(tt.Add(DateTime.Now, TimeSpan.FromHours(1), -1.0));
        Assert.Null(tt.Add(DateTime.Now, TimeSpan.FromHours(1), 10.0)); // > Max
    }

    [Fact]
    public void TestOverflow()
    {
        var oh = OpenHours.OpenHours.Parse("mo-fr 11:00-16:00");
        var tt = new TimeTable<double>(1.0, oh);
        var baseDate = new DateTime(2019, 3, 12, 11, 0, 0, DateTimeKind.Utc);

        tt = tt.Add(baseDate, TimeSpan.FromHours(5), 1.0)!;
        Assert.NotNull(tt);

        var fail = tt.Add(baseDate.AddHours(1), TimeSpan.FromMilliseconds(1), 0.0001);
        Assert.Null(fail);
    }

    [Fact]
    public void TestAdd_OverlappingAndSimplifying()
    {
        var oh = OpenHours.OpenHours.Parse("mo-fr 11:00-16:00");
        var tt = new TimeTable<int>(5, oh);
        var date = new DateTime(2019, 3, 12, 11, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 5; i++)
        {
            var res1 = tt.Add(date, TimeSpan.FromHours(2), 1);
            Assert.NotNull(res1);
            tt = res1;

            var res2 = tt.Add(date.AddHours(2), TimeSpan.FromHours(2), 1);
            Assert.NotNull(res2);
            tt = res2;
        }

        var fail = tt.Add(date, TimeSpan.FromHours(2), 1);
        Assert.Null(fail);
    }

    [Fact]
    public void TestAdd_RangesOverlapAtSameTime()
    {
        var oh = OpenHours.OpenHours.Parse("mo-fr 11:00-16:00");
        var tt = new TimeTable<int>(2, oh);
        var date = new DateTime(2019, 3, 12, 11, 0, 0, DateTimeKind.Utc);

        var res = tt.Add(date, TimeSpan.FromHours(1), 1);
        Assert.NotNull(res);
        tt = res;

        res = tt.Add(date, TimeSpan.FromHours(1), 1);
        Assert.NotNull(res);
        tt = res;

        var fail = tt.Add(date, TimeSpan.FromHours(1), 1);
        Assert.Null(fail);
    }

    [Fact]
    public void TestWhen_FindNextAvailableSlot()
    {
        var oh = OpenHours.OpenHours.Parse("mo-fr 11:00-16:00");
        var tt = new TimeTable<int>(2, oh);
        var d = new DateTime(2019, 3, 12, 10, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 100; i++)
        {
            var when = tt.When(d, TimeSpan.FromHours(1), 1);
            Assert.NotNull(when);
            if (when.Value > d)
            {
                d = when.Value;
            }

            var next = tt.Add(when.Value, TimeSpan.FromHours(1), 1);
            Assert.NotNull(next);
            tt = next;
        }
    }

    [Fact]
    public void TestWhen_ExceedingMaxCapacity_ReturnsNull()
    {
        var oh = OpenHours.OpenHours.Parse("mo-fr 11:00-16:00");
        var tt = new TimeTable<int>(10, oh);
        var d = new DateTime(2019, 3, 12, 10, 0, 0, DateTimeKind.Utc);

        var when = tt.When(d, TimeSpan.FromHours(1), 12);
        Assert.Null(when);
    }

    [Fact]
    public void TestMerge_SuccessAndFailure()
    {
        var oh = OpenHours.OpenHours.Parse("mo-fr 11:00-16:00");
        var tt = new TimeTable<int>(10, oh);
        var d = new DateTime(2019, 3, 12, 11, 0, 0, DateTimeKind.Utc);

        var t1 = tt.Add(d, TimeSpan.FromHours(1), 4);
        Assert.NotNull(t1);
        var t2 = t1.Clone();
        var merged = t1.Merge(t2);
        Assert.NotNull(merged);

        var t3 = tt.Add(d, TimeSpan.FromHours(1), 6);
        Assert.NotNull(t3);
        var t4 = t3.Clone();
        var mergedFail = t3.Merge(t4);
        Assert.Null(mergedFail);
    }

    [Fact]
    public void TestPointComparisonAndEquality()
    {
        var time1 = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc);
        var p1 = new Point<double>(time1, 2.5);
        var p2 = new Point<double>(time1, 2.5);
        var p3 = new Point<double>(time1.AddHours(1), 2.5);

        Assert.Equal(p1, p2);
        Assert.NotEqual(p1, p3);
        Assert.Equal(p1.GetHashCode(), p2.GetHashCode());
        Assert.True(p1.CompareTo(p3) < 0);
    }

    [Fact]
    public void TestJsonSerializationRoundtrip()
    {
        var tt = new TimeTable<double>(10.0);
        var now = new DateTime(2026, 8, 24, 9, 0, 0);
        var added = tt.Add(now, TimeSpan.FromHours(1), 2.5);
        Assert.NotNull(added);

        string json = JsonSerializer.Serialize(added);
        var deserialized = JsonSerializer.Deserialize<TimeTable<double>>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(10.0, deserialized.Max);
        Assert.Equal(2, deserialized.Count);
        Assert.Equal(2.5, deserialized.Points[0].Val);
        Assert.Equal(-2.5, deserialized.Points[1].Val);
        Assert.Equal(added, deserialized);
    }
}
