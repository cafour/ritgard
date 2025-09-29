namespace Ritgard.Mining;

public enum MergeableState
{
    Unknown,
    Dirty,
    Blocked,
    Behind,
    Unstable,
    HasHooks,
    Clean,
    Draft
}
