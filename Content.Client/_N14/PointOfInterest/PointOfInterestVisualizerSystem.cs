using Content.Shared._N14.PointOfInterest;
using Robust.Client.GameObjects;

namespace Content.Client._N14.PointOfInterest;

/// <summary>
/// Handles visual updates for point of interest entities on the client.
/// </summary>
public sealed class PointOfInterestVisualizerSystem : VisualizerSystem<PointOfInterestComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, PointOfInterestComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!args.Sprite.LayerMapTryGet("flag", out var flagLayer))
            return;

        if (!args.Sprite.LayerMapTryGet("flagpole", out var flagpoleLayer))
            return;

        // Get capture state and progress
        if (!AppearanceSystem.TryGetData<CaptureState>(uid, PointOfInterestVisuals.State, out var state, args.Component))
            return;

        if (!AppearanceSystem.TryGetData<float>(uid, PointOfInterestVisuals.CaptureProgress, out var progress, args.Component))
            progress = 0f;

        // Try to get the faction
        string? factionId = null;
        AppearanceSystem.TryGetData<string>(uid, PointOfInterestVisuals.Faction, out factionId, args.Component);

        // Try to get the animation flag
        if (!AppearanceSystem.TryGetData<int>(uid, PointOfInterestVisuals.AnimateFlag, out var animateFlag, args.Component))
            animateFlag = 1; // Default to animated

        // Always show the flagpole
        args.Sprite.LayerSetVisible(flagpoleLayer, true);
        args.Sprite.LayerSetRSI(flagpoleLayer, new Robust.Shared.Utility.ResPath("_native-fallout/Objects/Misc/Points/Flagpole/flagpole.rsi"));
        args.Sprite.LayerSetState(flagpoleLayer, "empty");

        // Determine flag state based on capture progress
        string flagState = "empty";
        
        // Determine which faction's RSI to use
        string? capturingFaction = null;
        
        // Use faction from appearance data if available and valid
        if (!string.IsNullOrEmpty(factionId) && component.FactionFlags.ContainsKey(factionId))
        {
            capturingFaction = factionId;
        }

        switch (state)
        {
            case CaptureState.Neutral:
                // No flag - hide the flag layer
                args.Sprite.LayerSetVisible(flagLayer, false);
                return;

            case CaptureState.Owned:
                // Flag fully raised - use faction RSI
                if (capturingFaction != null)
                {
                    args.Sprite.LayerSetVisible(flagLayer, true);
                    UpdateFactionRSI(args.Sprite, flagLayer, capturingFaction);
                    // Use animated state if AnimateFlag is 1, otherwise use static "top"
                    flagState = animateFlag == 1 ? "top-waving" : "top";
                }
                else
                {
                    args.Sprite.LayerSetVisible(flagLayer, false);
                    return;
                }
                break;

            case CaptureState.Contested_Lowering:
                // Flag being lowered: use owning faction's RSI (the one being lowered)
                if (capturingFaction != null)
                {
                    args.Sprite.LayerSetVisible(flagLayer, true);
                    UpdateFactionRSI(args.Sprite, flagLayer, capturingFaction);
                    
                    // Progress -> State mapping (lowering):
                    // 1.0 - 0.66 = top
                    // 0.66 - 0.33 = middle
                    // 0.33 - 0.01 = bottom
                    // 0.01 - 0.0 = empty (almost down)
                    if (progress >= 0.66f)
                        flagState = "top";
                    else if (progress >= 0.33f)
                        flagState = "middle";
                    else if (progress > 0.01f)
                        flagState = "bottom";
                    else
                        flagState = "empty";
                }
                else
                {
                    // No valid faction - hide flag
                    args.Sprite.LayerSetVisible(flagLayer, false);
                    return;
                }
                break;

            case CaptureState.Contested_Raising:
                // Flag being raised: use capturing faction's RSI
                if (capturingFaction != null)
                {
                    UpdateFactionRSI(args.Sprite, flagLayer, capturingFaction);
                    
                    // Progress -> State mapping:
                    // 0.0 - 0.01 = empty (just starting)
                    // 0.01 - 0.33 = bottom
                    // 0.33 - 0.66 = middle
                    // 0.66 - 1.0 = top
                    if (progress <= 0.01f)
                    {
                        // Just starting - hide flag
                        args.Sprite.LayerSetVisible(flagLayer, false);
                        return;
                    }
                    else if (progress < 0.33f)
                    {
                        args.Sprite.LayerSetVisible(flagLayer, true);
                        flagState = "bottom";
                    }
                    else if (progress < 0.66f)
                    {
                        args.Sprite.LayerSetVisible(flagLayer, true);
                        flagState = "middle";
                    }
                    else
                    {
                        args.Sprite.LayerSetVisible(flagLayer, true);
                        flagState = "top";
                    }
                }
                else
                {
                    // No valid faction - hide flag
                    args.Sprite.LayerSetVisible(flagLayer, false);
                    return;
                }
                break;
        }

        args.Sprite.LayerSetState(flagLayer, flagState);
    }

    private void UpdateFactionRSI(SpriteComponent sprite, int layer, string factionId)
    {
        // Map faction IDs to RSI paths
        var rsiPath = factionId switch
        {
            "NCR" => "_native-fallout/Objects/Misc/Points/NCR/flag.rsi",
            "BrotherhoodMidwest" => "_native-fallout/Objects/Misc/Points/BOS/flag.rsi",
            "CaesarLegion" => "_native-fallout/Objects/Misc/Points/Legion/flag.rsi",
            "Tribal" => "_native-fallout/Objects/Misc/Points/Tribe fish/flag.rsi",
            _ => "_native-fallout/Objects/Misc/Points/Flagpole/flagpole.rsi" // Fallback to empty flagpole
        };

        sprite.LayerSetRSI(layer, new Robust.Shared.Utility.ResPath(rsiPath));
    }
}
