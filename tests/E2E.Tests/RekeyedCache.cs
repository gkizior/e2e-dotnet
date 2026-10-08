// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests;

/// <summary>A cache directory whose one recording of the billing upgrade step sits under an old key.</summary>
internal static class RekeyedCache
{
    public static readonly string OldKey = new('b', 64);

    // Records the upgrade step and returns its entry.
    public static async Task<CacheEntry> RecordAsync(string directory)
    {
        await using (var session = await ConfigTests.StartAsync(directory, CacheMode.ReadWrite, strict: false, "Upgrade to Pro", () => { }))
        {
            await ConfigTests.UpgradeAsync(session);
            session.Complete();
        }

        var key = Path.GetFileNameWithoutExtension(Directory.GetFiles(directory).Single());
        return new FileStepCache(directory).Read(key).Entry!;
    }

    // Moves the only entry under OldKey, as a change to the key rules would leave it, after applying change.
    public static void Rekey(string directory, Action<CacheEntry> change)
    {
        var cache = new FileStepCache(directory);
        var key = Path.GetFileNameWithoutExtension(Directory.GetFiles(directory).Single());
        var entry = cache.Read(key).Entry!;
        change(entry);
        cache.Write(OldKey, entry);
        cache.Delete(key);
    }

    // Runs the upgrade step under strict mode and asserts that it ran live.
    public static async Task AssertRunsLiveAsync(string directory)
    {
        var calls = 0;
        await using (var session = await ConfigTests.StartAsync(directory, CacheMode.ReadOnly, strict: true, "Upgrade to Pro", () => calls++))
        {
            await ConfigTests.UpgradeAsync(session);
            Assert.Equal(1, session.Missed);
            session.Complete();
        }

        Assert.True(calls > 0);
    }
}
