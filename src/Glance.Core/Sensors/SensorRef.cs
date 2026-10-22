namespace Glance.Core.Sensors;

public readonly record struct SensorRef(bool IsRole, string Value)
{
    private const string RolePrefix = "role:";

    public static SensorRef Parse(string s) =>
        s.StartsWith(RolePrefix, StringComparison.Ordinal) ? new(true, s[RolePrefix.Length..]) : new(false, s);

    public override string ToString() => IsRole ? RolePrefix + Value : Value;
}
