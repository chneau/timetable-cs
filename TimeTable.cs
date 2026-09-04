using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chneau.TimeTable;

/// <summary>
/// Defines a temporal constraint provider that determines whether or when
/// a duration of time is permissible (e.g. within business opening hours).
/// </summary>
public interface IWhenable
{
    /// <summary>
    /// Returns the soonest moment at or after <paramref name="from"/> where the entire
    /// <paramref name="duration"/> satisfies the constraint, or <c>null</c> if no such time exists.
    /// </summary>
    DateTime? When(DateTime from, TimeSpan duration);
}

/// <summary>
/// A no-op constraint that allows any time and duration unconditionally.
/// </summary>
public sealed class NoopWhen : IWhenable
{
    public static readonly NoopWhen Instance = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime? When(DateTime from, TimeSpan duration) => from;
}

/// <summary>
/// Adapter wrapping an <see cref="OpenHours.OpenHours"/> instance into an <see cref="IWhenable"/>.
/// </summary>
public sealed class OpenHoursWhenable : IWhenable
{
    private readonly Chneau.OpenHours.OpenHours _openHours;

    public OpenHoursWhenable(Chneau.OpenHours.OpenHours openHours)
    {
        _openHours = openHours ?? throw new ArgumentNullException(nameof(openHours));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime? When(DateTime from, TimeSpan duration) => _openHours.When(from, duration);
}

/// <summary>
/// Represents a capacity delta at a specific point in time.
/// </summary>
/// <typeparam name="T">The numeric type representing capacity.</typeparam>
public readonly record struct Point<T>(DateTime Time, T Val) : IComparable<Point<T>>
    where T : INumber<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CompareTo(Point<T> other)
    {
        int cmp = Time.CompareTo(other.Time);
        if (cmp != 0) return cmp;
        return Val.CompareTo(other.Val);
    }
}

/// <summary>
/// Immutable, high-performance capacity timeline and constraint evaluation engine.
/// </summary>
/// <typeparam name="T">The numeric type representing capacity.</typeparam>
[JsonConverter(typeof(TimeTableJsonConverterFactory))]
public sealed class TimeTable<T> : IEquatable<TimeTable<T>>
    where T : INumber<T>
{
    private readonly Point<T>[] _rel;
    private readonly IWhenable _constraint;
    private readonly T _max;

    public T Max => _max;
    public IWhenable Constraint => _constraint;
    public ReadOnlySpan<Point<T>> Rel => _rel.AsSpan();
    public int Count => _rel.Length;
    public Point<T>[] Points => (Point<T>[])_rel.Clone();

    public TimeTable(T max, IWhenable? constraint = null)
        : this(max, constraint, Array.Empty<Point<T>>())
    {
    }

    public TimeTable(T max, Chneau.OpenHours.OpenHours openHours)
        : this(max, new OpenHoursWhenable(openHours), Array.Empty<Point<T>>())
    {
    }

    internal TimeTable(T max, IWhenable? constraint, Point<T>[] rel)
    {
        if (max < T.Zero)
            throw new ArgumentOutOfRangeException(nameof(max), "Capacity max must not be negative.");

        _max = max;
        _constraint = constraint ?? NoopWhen.Instance;
        _rel = rel;
    }

    internal static Point<T>[] SimplifyArray(Point<T>[] points)
    {
        return Simplify(points.AsSpan());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CheckPoints(ReadOnlySpan<Point<T>> rel, T max)
    {
        T current = T.Zero;
        for (int i = 0; i < rel.Length; i++)
        {
            current += rel[i].Val;
            if (current > max)
            {
                return false;
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point<T>[] Simplify(Span<Point<T>> points)
    {
        if (points.IsEmpty)
            return Array.Empty<Point<T>>();

        // In-place compaction of identical timestamps
        int write = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (write == 0)
            {
                points[write++] = points[i];
                continue;
            }

            if (points[i].Time == points[write - 1].Time)
            {
                T newVal = points[write - 1].Val + points[i].Val;
                if (newVal == T.Zero)
                {
                    write--; // Cancel out back to 0
                }
                else
                {
                    points[write - 1] = new Point<T>(points[write - 1].Time, newVal);
                }
            }
            else
            {
                points[write++] = points[i];
            }
        }

        if (write == 0)
            return Array.Empty<Point<T>>();

        return points[..write].ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (Point<T>[] SortedPoints, bool Ok) CheckRange(DateTime from, TimeSpan duration, T cap)
    {
        int n = _rel.Length;
        var list = new Point<T>[n + 2];
        _rel.CopyTo(list.AsSpan());
        list[n] = new Point<T>(from, cap);
        list[n + 1] = new Point<T>(from.Add(duration), -cap);

        Array.Sort(list);
        return (list, CheckPoints(list, _max));
    }

    /// <summary>
    /// Evaluates if adding <paramref name="cap"/> capacity from <paramref name="from"/>
    /// for <paramref name="duration"/> fits within the maximum limit and constraint.
    /// If valid, returns a new immutable <see cref="TimeTable{T}"/>; otherwise, returns <c>null</c>.
    /// </summary>
    public TimeTable<T>? Add(DateTime from, TimeSpan duration, T cap)
    {
        if (cap < T.Zero || cap > _max)
            return null;

        if (duration <= TimeSpan.Zero)
            return null;

        DateTime? validStart = _constraint.When(from, duration);
        if (!validStart.HasValue || validStart.Value != from)
            return null;

        var (candidate, ok) = CheckRange(from, duration, cap);
        if (!ok)
            return null;

        var simplified = Simplify(candidate);
        return new TimeTable<T>(_max, _constraint, simplified);
    }

    /// <summary>
    /// Clones the current timetable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TimeTable<T> Clone() => this;

    /// <summary>
    /// Merges two timetables together, validating that combined timeline capacity does not exceed Max.
    /// Returns the combined <see cref="TimeTable{T}"/> if valid; otherwise, returns <c>null</c>.
    /// </summary>
    public TimeTable<T>? Merge(TimeTable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);

        int totalLen = _rel.Length + other._rel.Length;
        if (totalLen == 0)
            return new TimeTable<T>(_max, _constraint, Array.Empty<Point<T>>());

        var merged = new Point<T>[totalLen];
        _rel.CopyTo(merged.AsSpan(0, _rel.Length));
        other._rel.CopyTo(merged.AsSpan(_rel.Length, other._rel.Length));

        Array.Sort(merged);
        var simplified = Simplify(merged);

        if (!CheckPoints(simplified, _max))
            return null;

        return new TimeTable<T>(_max, _constraint, simplified);
    }

    /// <summary>
    /// Returns the earliest moment at or after <paramref name="from"/> where <paramref name="cap"/>
    /// capacity can be reserved for <paramref name="duration"/> without violating constraints or max capacity.
    /// Returns <c>null</c> if no such time is available.
    /// </summary>
    public DateTime? When(DateTime from, TimeSpan duration, T cap)
    {
        if (cap < T.Zero || cap > _max)
            return null;

        if (duration <= TimeSpan.Zero)
            return null;

        // Check if immediately feasible
        DateTime? candidate = _constraint.When(from, duration);
        if (candidate.HasValue)
        {
            from = candidate.Value;
        }
        else
        {
            return null;
        }

        var (_, ok) = CheckRange(from, duration, cap);
        if (ok)
            return from;

        // Traverse timeline points
        for (int i = 0; i < _rel.Length; i++)
        {
            if (_rel[i].Time <= from)
                continue;

            from = _rel[i].Time;
            candidate = _constraint.When(from, duration);
            if (!candidate.HasValue)
                continue;

            from = candidate.Value;
            (_, ok) = CheckRange(from, duration, cap);
            if (ok)
                return from;
        }

        return null;
    }

    public bool Equals(TimeTable<T>? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        if (_max != other._max) return false;
        if (_rel.Length != other._rel.Length) return false;

        for (int i = 0; i < _rel.Length; i++)
        {
            if (_rel[i] != other._rel[i])
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is TimeTable<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_max);
        hash.Add(_rel.Length);
        for (int i = 0; i < _rel.Length; i++)
        {
            hash.Add(_rel[i]);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Static factory methods for creating <see cref="TimeTable{T}"/> instances.
/// </summary>
public static class TimeTable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeTable<T> Create<T>(T max, IWhenable? constraint = null) where T : INumber<T>
        => new(max, constraint);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeTable<T> Create<T>(T max, Chneau.OpenHours.OpenHours openHours) where T : INumber<T>
        => new(max, openHours);
}

public sealed class TimeTableJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType) return false;
        return typeToConvert.GetGenericTypeDefinition() == typeof(TimeTable<>);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type itemType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(TimeTableJsonConverter<>).MakeGenericType(itemType);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}

public sealed class TimeTableJsonConverter<T> : JsonConverter<TimeTable<T>>
    where T : INumber<T>
{
    public override TimeTable<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Expected StartObject");

        T? max = default;
        bool maxFound = false;
        List<Point<T>> points = new();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string? prop = reader.GetString();
                reader.Read();

                if (string.Equals(prop, "max", StringComparison.OrdinalIgnoreCase))
                {
                    max = JsonSerializer.Deserialize<T>(ref reader, options);
                    maxFound = true;
                }
                else if (string.Equals(prop, "points", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.EndArray) break;
                            if (reader.TokenType == JsonTokenType.StartObject)
                            {
                                DateTime time = default;
                                T? val = default;
                                while (reader.Read())
                                {
                                    if (reader.TokenType == JsonTokenType.EndObject) break;
                                    if (reader.TokenType == JsonTokenType.PropertyName)
                                    {
                                        string? pName = reader.GetString();
                                        reader.Read();
                                        if (string.Equals(pName, "time", StringComparison.OrdinalIgnoreCase))
                                        {
                                            time = reader.GetDateTime();
                                        }
                                        else if (string.Equals(pName, "val", StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(pName, "value", StringComparison.OrdinalIgnoreCase))
                                        {
                                            val = JsonSerializer.Deserialize<T>(ref reader, options);
                                        }
                                    }
                                }
                                if (val != null)
                                {
                                    points.Add(new Point<T>(time, val));
                                }
                            }
                        }
                    }
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        if (!maxFound || max == null)
            throw new JsonException("TimeTable JSON missing required 'max' property.");

        var arr = points.ToArray();
        Array.Sort(arr);
        var simplified = TimeTable<T>.SimplifyArray(arr);
        return new TimeTable<T>(max, NoopWhen.Instance, simplified);
    }

    public override void Write(Utf8JsonWriter writer, TimeTable<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("max");
        JsonSerializer.Serialize(writer, value.Max, options);

        writer.WriteStartArray("points");
        foreach (var p in value.Rel)
        {
            writer.WriteStartObject();
            writer.WriteString("time", p.Time.ToString("yyyy-MM-ddTHH:mm:ss"));
            writer.WritePropertyName("val");
            JsonSerializer.Serialize(writer, p.Val, options);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
