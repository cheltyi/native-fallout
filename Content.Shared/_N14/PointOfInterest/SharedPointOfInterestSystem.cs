using Robust.Shared.Serialization;

namespace Content.Shared._N14.PointOfInterest;

/// <summary>
/// Shared system for point of interest visualization.
/// </summary>
public abstract class SharedPointOfInterestSystem : EntitySystem
{
}

/// <summary>
/// Visuals for the point of interest capture state.
/// </summary>
[Serializable, NetSerializable]
public enum PointOfInterestVisuals : byte
{
    State,
    CaptureProgress,
    Faction,
}
