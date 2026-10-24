using System.Security.Principal;
using Glance.Platform.Windows;

namespace Glance.Platform.Windows.Tests;

/// <summary>An in-memory marker; <see cref="Log"/>, when given, records each change in order with the steps.</summary>
internal sealed class FakeMarker(string? value = null, List<string>? log = null) : ITakeoverMarker
{
    public string? Value { get; private set; } = value;

    public string? Read() => Value;

    public void Write(string quarantineDirName)
    {
        log?.Add($"marker write {quarantineDirName}");
        Value = quarantineDirName;
    }

    public void Clear()
    {
        log?.Add("marker clear");
        Value = null;
    }
}

/// <summary>
/// Steps that change no permissions (so they run unelevated): the root reads as <paramref name="rootDescriptor"/>,
/// each Secure is only recorded, links are really deleted. <paramref name="failOnRoot"/> makes securing the root throw.
/// </summary>
internal sealed class FakeEnforceSteps(byte[] rootDescriptor, List<string>? log = null, bool failOnRoot = false) : DataDirectoryAcl.IEnforceSteps
{
    public List<string> Log { get; } = log ?? new();

    /// <summary>A root as a local user would leave it: owned by Users.</summary>
    public static byte[] UserOwnedRoot => DataDirectoryAcl.Descriptor(DataDirectoryAcl.EntryKind.Root, new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));

    public static byte[] HardenedRoot => DataDirectoryAcl.Descriptor(DataDirectoryAcl.EntryKind.Root, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));

    public byte[]? ReadRoot(DirectoryInfo root) => rootDescriptor;

    public bool Secure(FileSystemInfo entry, string rel, DataDirectoryAcl.EntryKind kind, bool resetAll, List<string> actions, ref bool changed)
    {
        Log.Add($"secure {kind} {rel}{(resetAll ? " (all)" : "")}");
        if (failOnRoot && kind == DataDirectoryAcl.EntryKind.Root) throw new UnauthorizedAccessException("cannot take ownership");
        return true;
    }

    public bool RemoveIfLink(FileSystemInfo entry, string rel, List<string> actions, ref bool changed) =>
        DataDirectoryAcl.RemoveIfLink(entry, rel, actions, ref changed);
}
