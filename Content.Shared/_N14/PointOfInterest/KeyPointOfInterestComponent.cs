using Robust.Shared.GameStates;
using Robust.Shared.Audio;

namespace Content.Shared._N14.PointOfInterest;

/// <summary>
/// Represents a key (main) point of interest that can only be captured when the opposing faction has no secondary points.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class KeyPointOfInterestComponent : Component
{
    /// <summary>
    /// Whether this key point is currently locked (cannot be captured).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsLocked = true;

   /// <summary>
   /// Whether this key point has already been captured at least once.
   /// Used to suppress repeated global victory messages on recaptures.
   /// </summary>
   [DataField, AutoNetworkedField]
   public bool HasBeenCaptured = false;

   /// <summary>
   /// Sound to play when this key point is captured.
   /// </summary>
   [DataField]
   public SoundSpecifier? VictorySound;
}
