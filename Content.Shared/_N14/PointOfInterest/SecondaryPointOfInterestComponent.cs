using Robust.Shared.GameStates;

namespace Content.Shared._N14.PointOfInterest;

/// <summary>
/// Represents a secondary point of interest. When a faction loses all secondary points, the enemy key point becomes unlocked.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SecondaryPointOfInterestComponent : Component
{
}
