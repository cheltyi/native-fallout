using Content.Shared._N14.PointOfInterest;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using System.Linq;
using Robust.Shared.GameObjects;

namespace Content.Server._N14.PointOfInterest;

/// <summary>
/// Handles the capture mechanics for points of interest.
/// </summary>
public sealed class PointOfInterestSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;

    private const float UpdateInterval = 1f; // Update every second
    private float _timeSinceUpdate = 0f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PointOfInterestComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<PointOfInterestComponent, ComponentShutdown>(OnComponentShutdown);
    }

    private void OnComponentInit(EntityUid uid, PointOfInterestComponent component, ComponentInit args)
    {
        // Initialize with neutral state
        component.State = CaptureState.Neutral;
        component.CaptureProgress = 0f;
    }

    private void OnComponentShutdown(EntityUid uid, PointOfInterestComponent component, ComponentShutdown args)
    {
        // No cleanup needed - flagpole is the entity itself now
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _timeSinceUpdate += frameTime;
        if (_timeSinceUpdate < UpdateInterval)
            return;

        _timeSinceUpdate = 0f;

        // Process all capture points
        var query = EntityQueryEnumerator<PointOfInterestComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var poi, out var xform))
        {
            UpdateCapturePoint(uid, poi, xform, UpdateInterval);
        }
    }

    private void UpdateCapturePoint(EntityUid uid, PointOfInterestComponent poi, TransformComponent xform, float deltaTime)
    {
        // Get all entities in the capture radius
        var coords = _transform.GetMapCoordinates(uid, xform);
        var entitiesInRange = _lookup.GetEntitiesInRange(coords, poi.CaptureRadius);

        // Find faction members in the area
        var factionCounts = new Dictionary<ProtoId<NpcFactionPrototype>, int>();
        
        foreach (var entity in entitiesInRange)
        {
            // Only count entities with faction membership
            if (!TryComp<NpcFactionMemberComponent>(entity, out var factionComp))
                continue;

            // Count members of each faction - but ONLY if the faction is configured for this point
            foreach (var faction in factionComp.Factions)
            {
                // Check if this faction is in the FactionFlags dictionary
                if (!poi.FactionFlags.ContainsKey(faction))
                    continue; // Skip factions not configured for this point
                
                if (!factionCounts.ContainsKey(faction))
                    factionCounts[faction] = 0;
                factionCounts[faction]++;
            }
        }

        // Process capture logic
        ProcessCapture(uid, poi, factionCounts, deltaTime);
        
        // Update appearance
        UpdateAppearance(uid, poi);
    }

    private void ProcessCapture(EntityUid uid, PointOfInterestComponent poi, Dictionary<ProtoId<NpcFactionPrototype>, int> factionCounts, float deltaTime)
    {
        // Remove the owning faction from the counts to only consider attackers/capturers
        if (poi.OwningFaction != null && factionCounts.ContainsKey(poi.OwningFaction.Value))
        {
            var defenders = factionCounts[poi.OwningFaction.Value];
            factionCounts.Remove(poi.OwningFaction.Value);
            
            // If defenders are present and there are attackers, contest is blocked
            if (defenders > 0 && factionCounts.Count > 0)
            {
                // Defenders block capture attempts
                ResetCapture(uid, poi);
                return;
            }
        }

        // Get the dominant attacking/capturing faction
        if (!factionCounts.Any())
        {
            // No one in capture zone - start reset timer
            HandleEmptyZone(uid, poi, deltaTime);
            return;
        }
        
        var dominantFaction = factionCounts.OrderByDescending(kvp => kvp.Value).First();
        
        if (dominantFaction.Value == 0)
        {
            // No one in capture zone - start reset timer
            HandleEmptyZone(uid, poi, deltaTime);
            return;
        }

        // Someone is in the zone, reset the accumulator
        poi.ResetAccumulator = 0f;

        // Handle capture based on current state
        switch (poi.State)
        {
            case CaptureState.Neutral:
                HandleNeutralCapture(uid, poi, dominantFaction.Key, deltaTime);
                break;

            case CaptureState.Owned:
                if (dominantFaction.Key != poi.OwningFaction)
                {
                    // Enemy is starting to lower the flag
                    HandleFlagLowering(uid, poi, dominantFaction.Key, deltaTime);
                }
                break;

            case CaptureState.Contested_Lowering:
                if (dominantFaction.Key == poi.CapturingFaction)
                {
                    HandleFlagLowering(uid, poi, dominantFaction.Key, deltaTime);
                }
                else
                {
                    // Different faction entered, reset
                    ResetCapture(uid, poi);
                }
                break;

            case CaptureState.Contested_Raising:
                if (dominantFaction.Key == poi.CapturingFaction)
                {
                    HandleFlagRaising(uid, poi, dominantFaction.Key, deltaTime);
                }
                else
                {
                    // Different faction entered, reset
                    ResetCapture(uid, poi);
                }
                break;
        }

        Dirty(uid, poi);
    }

    private void HandleNeutralCapture(EntityUid uid, PointOfInterestComponent poi, ProtoId<NpcFactionPrototype> faction, float deltaTime)
    {
        poi.State = CaptureState.Contested_Raising;
        poi.CapturingFaction = faction;
        
        // Increase capture progress
        poi.CaptureProgress += deltaTime / poi.CaptureTime;

        if (poi.CaptureProgress >= 1.0f)
        {
            // Capture complete!
            CompleteFlagRaise(uid, poi, faction);
        }
    }

    private void HandleFlagLowering(EntityUid uid, PointOfInterestComponent poi, ProtoId<NpcFactionPrototype> attackingFaction, float deltaTime)
    {
        poi.State = CaptureState.Contested_Lowering;
        poi.CapturingFaction = attackingFaction;

        // Decrease capture progress (lowering the enemy flag)
        poi.CaptureProgress -= deltaTime / poi.CaptureTime;

        if (poi.CaptureProgress <= 0.0f)
        {
            // Flag completely lowered - point becomes neutral
            poi.CaptureProgress = 0f;
            poi.State = CaptureState.Contested_Raising; // Now start raising attacker's flag
            
            _popup.PopupEntity($"The {poi.OwningFaction} flag has been lowered!", uid, PopupType.LargeCaution);
            
            poi.OwningFaction = null;
        }
    }

    private void HandleFlagRaising(EntityUid uid, PointOfInterestComponent poi, ProtoId<NpcFactionPrototype> faction, float deltaTime)
    {
        // Increase capture progress (raising new flag)
        poi.CaptureProgress += deltaTime / poi.CaptureTime;

        if (poi.CaptureProgress >= 1.0f)
        {
            // Capture complete!
            CompleteFlagRaise(uid, poi, faction);
        }
    }

    private void CompleteFlagRaise(EntityUid uid, PointOfInterestComponent poi, ProtoId<NpcFactionPrototype> faction)
    {
        poi.OwningFaction = faction;
        poi.State = CaptureState.Owned;
        poi.CaptureProgress = 1.0f;
        poi.CapturingFaction = null;

        _popup.PopupEntity($"Point of interest captured by {faction}!", uid, PopupType.Large);
    }

    private void HandleEmptyZone(EntityUid uid, PointOfInterestComponent poi, float deltaTime)
    {
        // Only start reset timer if we're in a contested state
        if (poi.State != CaptureState.Contested_Lowering && poi.State != CaptureState.Contested_Raising)
            return;

        poi.ResetAccumulator += deltaTime;

        if (poi.ResetAccumulator >= poi.ResetTime)
        {
            // Reset time reached - restore to previous state
            ResetCapture(uid, poi);
            poi.ResetAccumulator = 0f;
        }
    }

    private void ResetCapture(EntityUid uid, PointOfInterestComponent poi)
    {
        // Reset to previous state
        if (poi.OwningFaction != null)
        {
            // Point was owned - return to owned state with full progress
            poi.State = CaptureState.Owned;
            poi.CaptureProgress = 1.0f;
        }
        else
        {
            // Point was neutral - return to neutral state
            poi.State = CaptureState.Neutral;
            poi.CaptureProgress = 0f;
        }
        
        poi.CapturingFaction = null;
        poi.ResetAccumulator = 0f;
    }


    private void UpdateAppearance(EntityUid uid, PointOfInterestComponent poi)
    {
        // Update visual state based on capture progress
        _appearance.SetData(uid, PointOfInterestVisuals.State, poi.State);
        _appearance.SetData(uid, PointOfInterestVisuals.CaptureProgress, poi.CaptureProgress);
        
        // Set faction based on current state
        if (poi.State == CaptureState.Owned && poi.OwningFaction != null)
        {
            // Show owning faction's flag (fully raised)
            _appearance.SetData(uid, PointOfInterestVisuals.Faction, poi.OwningFaction.Value.Id);
        }
        else if (poi.State == CaptureState.Contested_Lowering && poi.OwningFaction != null)
        {
            // Show OWNING faction's flag being lowered
            _appearance.SetData(uid, PointOfInterestVisuals.Faction, poi.OwningFaction.Value.Id);
        }
        else if (poi.State == CaptureState.Contested_Raising && poi.CapturingFaction != null)
        {
            // Show CAPTURING faction's flag being raised
            _appearance.SetData(uid, PointOfInterestVisuals.Faction, poi.CapturingFaction.Value.Id);
        }
        else
        {
            // Neutral - no faction
            _appearance.SetData(uid, PointOfInterestVisuals.Faction, string.Empty);
        }
    }
}
