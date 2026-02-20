using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.NPC.Prototypes;

namespace Content.Shared._N14.PointOfInterest;

/// <summary>
/// Represents a capturable point of interest on the map.
/// Factions can capture it by standing in the area for a specified duration.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PointOfInterestComponent : Component
{
    /// <summary>
    /// The radius around this entity that counts as the capture zone.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CaptureRadius = 7f;

    /// <summary>
    /// Time required to capture an unclaimed point or neutralize an enemy point (in seconds).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CaptureTime = 180f; // 3 minutes

    /// <summary>
    /// Current faction that owns this point (null if neutral/unclaimed).
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<NpcFactionPrototype>? OwningFaction;

    /// <summary>
    /// The original owner of this point when it was first attacked.
    /// Used for victory announcements to correctly identify the defeated faction.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<NpcFactionPrototype>? OriginalOwner;

    /// <summary>
    /// Current capture progress (0.0 to 1.0).
    /// When lowering enemy flag: 1.0 -> 0.0
    /// When raising own flag: 0.0 -> 1.0
    /// </summary>
    [DataField, AutoNetworkedField]
    public float CaptureProgress = 0f;

    /// <summary>
    /// The faction currently attempting to capture this point.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<NpcFactionPrototype>? CapturingFaction;

    /// <summary>
    /// Current capture state of the point.
    /// </summary>
    [DataField, AutoNetworkedField]
    public CaptureState State = CaptureState.Neutral;

    /// <summary>
    /// Faction IDs that are allowed to capture this point.
    /// Maps faction ID to flag sprite identifier (for future use).
    /// </summary>
    [DataField]
    public Dictionary<string, string> FactionFlags = new();

    /// <summary>
    /// Time in seconds after which progress resets if no one is in the capture zone.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ResetTime = 30f; // 30 seconds

    /// <summary>
    /// Time accumulator for progress reset (counts up when zone is empty).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ResetAccumulator = 0f;

    /// <summary>
    /// Whether the flag should animate when fully raised (1 = animated, 0 = static).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int AnimateFlag = 1;
}

/// <summary>
/// Possible states for a capture point.
/// </summary>
[Serializable, NetSerializable]
public enum CaptureState : byte
{
    /// <summary>
    /// Point is not owned by any faction.
    /// </summary>
    Neutral,

    /// <summary>
    /// Point is owned by a faction (flag fully raised).
    /// </summary>
    Owned,

    /// <summary>
    /// Enemy faction is lowering the current flag.
    /// </summary>
    Contested_Lowering,

    /// <summary>
    /// Faction is raising their flag (after neutralizing enemy).
    /// </summary>
    Contested_Raising,
}
