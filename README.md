# timetable-cs

[![NuGet](https://img.shields.io/nuget/v/Chneau.TimeTable.svg)](https://www.nuget.org/packages/Chneau.TimeTable/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A modern, high-performance capacity and schedule constraint evaluation engine in C# (.NET 10).

Ported from [chneau/timetable](https://github.com/chneau/timetable) (Go) and [chneau/timetable-java](https://github.com/chneau/timetable-java) (Java 26), with full generic math (`INumber<T>`) support and seamless integration with [Chneau.OpenHours](https://github.com/chneau/openhours-cs).

## Features

- **Generic Capacity Math**: Works with any numeric type (`int`, `long`, `double`, `float`, `decimal`) via .NET `INumber<T>`.
- **Immutable Timeline Design**: Safe concurrency with structural sharing and state updates.
- **Schedule Constraint Integration**: Pluggable `IWhenable` interface directly compatible with `OpenHours` expressions.
- **Zero-Allocation Queries**: Fast capacity evaluations and constraint searches.
- **High-Performance JSON Serialization**: Custom `System.Text.Json` converter.

## Installation

```bash
dotnet add package Chneau.TimeTable
```

## Quick Start

```csharp
using Chneau.OpenHours;
using Chneau.TimeTable;

// 1. Create a timetable with an OpenHours schedule constraint
var oh = OpenHours.Parse("Mo-Fr 09:00-17:00");
var tt = new TimeTable<int>(max: 5, openHours: oh);

var mondayMorning = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);
var oneHour = TimeSpan.FromHours(1);

// 2. Reserve capacity
var updated = tt.Add(mondayMorning, oneHour, cap: 2);
if (updated != null)
{
    Console.WriteLine($"Reserved successfully! Current points: {updated.Count}");
}

// 3. Find the soonest available slot for a 2-hour task requiring 4 slots of capacity
DateTime? nextAvailable = tt.When(mondayMorning, TimeSpan.FromHours(2), cap: 4);
Console.WriteLine($"Next slot: {nextAvailable}");
```

## Benchmarks

Run benchmarks locally:

```bash
dotnet test -c Release --filter TestRunBenchmarkSuite --logger "console;verbosity=detailed"
```

Sample results (.NET 10 on AMD Ryzen 9):

| Benchmark | Calls | Total Time | Per Operation | Allocations |
| :--- | :--- | :--- | :--- | :--- |
| **Sequential Add** | 50,000 | 155 ms | **3.1 μs/op** | 920 B/op |
| **Capacity Search (`When`)** | 20,000 | 7.8 ms | **0.39 μs/op** | 105 B/op |
| **JSON Deserialization** | 10,000 | 36.5 ms | **3.6 μs/op** | 664 B/op |

## License

MIT © [chneau](https://github.com/chneau)
