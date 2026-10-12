// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Mobile.Limrun;

/// <summary>The iOS device a Limrun simulator imitates.</summary>
public enum LimrunIosModel
{
    Iphone,
    Ipad,
}

/// <summary>The Android device a Limrun emulator imitates.</summary>
public enum LimrunAndroidModel
{
    Phone,
    Tablet,
}

/// <summary>What <see cref="LimrunSimulators"/> asks of an iOS simulator.</summary>
public sealed record LimrunIosOptions
{
    /// <summary>The device to simulate. Absent, Limrun picks.</summary>
    public LimrunIosModel? Model { get; init; }
}

/// <summary>What <see cref="LimrunSimulators"/> asks of an Android emulator.</summary>
public sealed record LimrunAndroidOptions
{
    /// <summary>The device to emulate. Absent, Limrun picks.</summary>
    public LimrunAndroidModel? Model { get; init; }

    /// <summary>The Android version to run. Defaults to <c>15</c>.</summary>
    public string? OsVersion { get; init; }
}

/// <summary>What <see cref="LimrunSimulators"/> takes: where Limrun places the device and how long it may live.</summary>
public sealed record LimrunSimulatorsOptions
{
    /// <summary>Region preference, such as <c>eu</c> or <c>us-east1</c>. Limrun may overflow to another region.</summary>
    public string? Region { get; init; }

    /// <summary>Maximum device lifetime, as Limrun writes durations (<c>30m</c>, <c>1h</c>). Defaults to <c>1h</c>, including abandoned runs.</summary>
    public string? HardTimeout { get; init; }

    public LimrunIosOptions? Ios { get; init; }

    public LimrunAndroidOptions? Android { get; init; }
}
