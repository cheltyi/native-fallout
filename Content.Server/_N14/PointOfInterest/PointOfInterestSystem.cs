using Content.Shared._N14.PointOfInterest;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Chat;
using Robust.Shared.Player;
using Robust.Shared.Localization;
using Content.Shared.Examine;

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
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    private const float UpdateInterval = 1f; // Update every second
    private float _timeSinceUpdate = 0f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PointOfInterestComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<PointOfInterestComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<KeyPointOfInterestComponent, ComponentInit>(OnKeyPointInit);
        SubscribeLocalEvent<PointOfInterestComponent, ExaminedEvent>(OnExamined);
    }
    
    private void OnKeyPointInit(EntityUid uid, KeyPointOfInterestComponent component, ComponentInit args)
    {
        // Key points start locked
        component.IsLocked = true;
    }

    private void OnExamined(EntityUid uid, PointOfInterestComponent component, ExaminedEvent args)
    {
        var remainingTime = GetRemainingCaptureTime(component);
        
        if (component.State == CaptureState.Neutral)
        {
            args.PushMarkup(Loc.GetString("poi-examine-neutral", ("time", (int) MathF.Round(component.CaptureTime))));
        }
        else if (component.State == CaptureState.Owned)
        {
            var ownerName = GetFactionDisplayName(component.OwningFaction);
            args.PushMarkup(Loc.GetString("poi-examine-owned", ("faction", ownerName)));
        }
        else if (component.State == CaptureState.Contested_Raising)
        {
            var capturerName = GetFactionDisplayName(component.CapturingFaction);
            args.PushMarkup(Loc.GetString("poi-examine-raising", ("faction", capturerName), ("time", (int) MathF.Round(remainingTime))));
        }
        else if (component.State == CaptureState.Contested_Lowering)
        {
            var attackerName = GetFactionDisplayName(component.CapturingFaction);
            args.PushMarkup(Loc.GetString("poi-examine-lowering", ("faction", attackerName), ("time", (int) MathF.Round(remainingTime))));
        }
    }

    private float GetRemainingCaptureTime(PointOfInterestComponent poi)
    {
        var progress = poi.CaptureProgress;
        var totalTime = poi.CaptureTime;
        
        if (poi.State == CaptureState.Contested_Raising)
        {
            return (1.0f - progress) * totalTime;
        }
        else if (poi.State == CaptureState.Contested_Lowering)
        {
            return progress * totalTime;
        }
        
        return totalTime;
    }

    private string GetFactionDisplayName(ProtoId<NpcFactionPrototype>? faction)
    {
        if (faction == null) return "Неизвестная";
        
        return faction.Value.Id switch
        {
            "NCR" => "НКР",
            "BrotherhoodMidwest" => "Братство Стали",
            "CaesarLegion" => "Легион Цезаря",
            "Tribal" => "Племя",
            _ => faction.Value.Id
        };
    }

    private void OnComponentInit(EntityUid uid, PointOfInterestComponent component, ComponentInit args)
    {
        // Initialize state based on whether the point is already owned
        if (component.OwningFaction != null)
        {
            component.State = CaptureState.Owned;
            component.CaptureProgress = 1.0f;
        }
        else
        {
            component.State = CaptureState.Neutral;
            component.CaptureProgress = 0f;
        }
        
        // Update appearance immediately
        UpdateAppearance(uid, component);
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

        // First, check if any key points should be unlocked
        CheckKeyPointLocks();

        // Process all capture points
        var query = EntityQueryEnumerator<PointOfInterestComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var poi, out var xform))
        {
            UpdateCapturePoint(uid, poi, xform, UpdateInterval);
        }
    }
    
    private void CheckKeyPointLocks()
    {
        // Check for each faction if they have lost all secondary points
        var factionSecondaryPoints = new Dictionary<ProtoId<NpcFactionPrototype>, List<EntityUid>>();
        
        // Collect all secondary points grouped by owning faction
        var secondaryQuery = EntityQueryEnumerator<SecondaryPointOfInterestComponent, PointOfInterestComponent>();
        while (secondaryQuery.MoveNext(out var uid, out var secondary, out var poi))
        {
            if (poi.OwningFaction != null)
            {
                if (!factionSecondaryPoints.ContainsKey(poi.OwningFaction.Value))
                    factionSecondaryPoints[poi.OwningFaction.Value] = new List<EntityUid>();
                factionSecondaryPoints[poi.OwningFaction.Value].Add(uid);
            }
        }
        
        // Check all key points
        var keyQuery = EntityQueryEnumerator<KeyPointOfInterestComponent, PointOfInterestComponent>();
        while (keyQuery.MoveNext(out var keyUid, out var keyPoint, out var keyPoi))
        {
            // If this key point is owned by a faction
            if (keyPoi.OwningFaction != null)
            {
                var owningFaction = keyPoi.OwningFaction.Value;
                
                // Check if this faction has any secondary points left
                bool hasSecondaryPoints = factionSecondaryPoints.ContainsKey(owningFaction) && 
                                         factionSecondaryPoints[owningFaction].Count > 0;
                
                // If faction has no secondary points left, unlock ALL enemy key points
                if (!hasSecondaryPoints && keyPoint.IsLocked)
                {
                    UnlockKeyPoint(keyUid, keyPoint, keyPoi, owningFaction);
                }
                // If faction regains secondary points, lock their key point again
                else if (hasSecondaryPoints && !keyPoint.IsLocked && keyPoi.OwningFaction == owningFaction)
                {
                    LockKeyPoint(keyUid, keyPoint);
                }
            }
        }
    }
    
    private void UnlockKeyPoint(EntityUid uid, KeyPointOfInterestComponent keyPoint, PointOfInterestComponent poi, ProtoId<NpcFactionPrototype> defeatedFaction)
    {
        keyPoint.IsLocked = false;
        Dirty(uid, keyPoint);
        
        // Find who is attacking (who just captured the last secondary point)
        // We need to figure out who's attacking by checking all points
        ProtoId<NpcFactionPrototype>? attackingFaction = null;
        
        var secondaryQuery = EntityQueryEnumerator<SecondaryPointOfInterestComponent, PointOfInterestComponent>();
        while (secondaryQuery.MoveNext(out var suid, out var secondary, out var spoi))
        {
            // Find a faction that owns points but is NOT the defeated faction
            if (spoi.OwningFaction != null && spoi.OwningFaction != defeatedFaction)
            {
                attackingFaction = spoi.OwningFaction;
                break;
            }
        }
        
        // Send "last flag" message with attacker->defender combination
        if (attackingFaction != null)
        {
            var locKey = GetLastFlagLocKey(attackingFaction.Value, defeatedFaction);
            if (locKey != null)
            {
                var warningMessage = Loc.GetString(locKey);
                _chat.DispatchGlobalAnnouncement(warningMessage, colorOverride: Color.Red);
            }
        }
    }
    
    private string? GetLastFlagLocKey(ProtoId<NpcFactionPrototype> attacker, ProtoId<NpcFactionPrototype> defender)
    {
        // Format: poi-lastflag-{attacker}-{defender}
        var attackerKey = attacker.Id switch
        {
            "NCR" => "ncr",
            "BrotherhoodMidwest" => "bos",
            "CaesarLegion" => "legion",
            "Tribal" => "tribe",
            _ => null
        };
        
        var defenderKey = defender.Id switch
        {
            "NCR" => "ncr",
            "BrotherhoodMidwest" => "bos",
            "CaesarLegion" => "legion",
            "Tribal" => "tribe",
            _ => null
        };
        
        if (attackerKey != null && defenderKey != null)
            return $"poi-lastflag-{attackerKey}-{defenderKey}";
        
        return null;
    }
    
    private void LockKeyPoint(EntityUid uid, KeyPointOfInterestComponent keyPoint)
    {
        keyPoint.IsLocked = true;
        Dirty(uid, keyPoint);
    }

    private void UpdateCapturePoint(EntityUid uid, PointOfInterestComponent poi, TransformComponent xform, float deltaTime)
    {
        // Check if this is a locked key point
        if (TryComp<KeyPointOfInterestComponent>(uid, out var keyPoint) && keyPoint.IsLocked)
        {
            // Don't process capture for locked key points
            return;
        }
        
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
            
            _popup.PopupEntity(Loc.GetString("poi-popup-flag-lowered", ("faction", GetFactionDisplayName(poi.OwningFaction))), uid, PopupType.LargeCaution);
            
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

        _popup.PopupEntity(Loc.GetString("poi-popup-captured", ("faction", GetFactionDisplayName(faction))), uid, PopupType.Large);
        
        // Check if this is a key point - play victory sound and, on first capture, announcement
        if (TryComp<KeyPointOfInterestComponent>(uid, out var keyPoint))
        {
            HandleKeyPointCapture(uid, keyPoint, faction);
        }
        else
        {
            // This is a secondary point - play secondary point sound
            var secondarySound = GetVictorySoundForFaction(faction, false); // false = secondary point
            if (secondarySound != null)
            {
                _audio.PlayGlobal(secondarySound, Filter.Broadcast(), true);
            }
        }
    }
    
    private void HandleKeyPointCapture(EntityUid uid, KeyPointOfInterestComponent keyPoint, ProtoId<NpcFactionPrototype> capturingFaction)
    {
        // Always play the attacker's victory sound when a key point is captured (even on recaptures)
        var attackerSound = GetVictorySoundForFaction(capturingFaction, true);
        if (attackerSound != null)
            _audio.PlayGlobal(attackerSound, Filter.Broadcast(), true);

        // If this key point has already been captured before, suppress global 'destroying faction' announcements
        if (keyPoint.HasBeenCaptured)
            return;
        // After first capture, the key point becomes a regular point (unlocked forever)
        keyPoint.IsLocked = false;
        
        // Get the component to see who previously owned this point
        if (!TryComp<PointOfInterestComponent>(uid, out var poi))
            return;
        
        // The defender is the one who just lost the key point (previous owner from before this capture started)
        // We need to track this - for now, we'll determine it from the key point entity itself
        ProtoId<NpcFactionPrototype>? defeatedFaction = null;
        
        // Check all key points to find which faction this key point belongs to
        var keyQuery = EntityQueryEnumerator<KeyPointOfInterestComponent, PointOfInterestComponent>();
        while (keyQuery.MoveNext(out var kuid, out var kp, out var kpoi))
        {
            if (kuid == uid)
            {
                // This is our key point - check metadata or prototypes to determine which faction it was for
                // For now, we'll infer from remaining points
                var allFactions = new[] { "NCR", "BrotherhoodMidwest", "CaesarLegion", "Tribal" };
                foreach (var fid in allFactions)
                {
                    if (fid != capturingFaction.Id)
                    {
                        // Check if this faction has any points left
                        var hasPoints = false;
                        var checkQuery = EntityQueryEnumerator<PointOfInterestComponent>();
                        while (checkQuery.MoveNext(out var cuid, out var cpoi))
                        {
                            if (cpoi.OwningFaction?.Id == fid && cuid != uid)
                            {
                                hasPoints = true;
                                break;
                            }
                        }
                        
                        if (!hasPoints)
                        {
                            defeatedFaction = new ProtoId<NpcFactionPrototype>(fid);
                            break;
                        }
                    }
                }
                break;
            }
        }
        
        // Mark that this key point has had its first capture; subsequent captures won't announce destruction text
        keyPoint.HasBeenCaptured = true;
        
        // Send victory message with attacker->defender combination
        if (defeatedFaction != null)
        {
            var locKey = GetVictoryLocKey(capturingFaction, defeatedFaction.Value);
            if (locKey != null)
            {
                var message = Loc.GetString(locKey);
                _chat.DispatchGlobalAnnouncement(message, colorOverride: Color.Red);
            }
        }
    }
    
    private string? GetVictoryLocKey(ProtoId<NpcFactionPrototype> attacker, ProtoId<NpcFactionPrototype> defender)
    {
        // Format: poi-victory-{attacker}-{defender}
        var attackerKey = attacker.Id switch
        {
            "NCR" => "ncr",
            "BrotherhoodMidwest" => "bos",
            "CaesarLegion" => "legion",
            "Tribal" => "tribe",
            _ => null
        };
        
        var defenderKey = defender.Id switch
        {
            "NCR" => "ncr",
            "BrotherhoodMidwest" => "bos",
            "CaesarLegion" => "legion",
            "Tribal" => "tribe",
            _ => null
        };
        
        if (attackerKey != null && defenderKey != null)
            return $"poi-victory-{attackerKey}-{defenderKey}";
        
        return null;
    }
    
    private SoundSpecifier? GetVictorySoundForFaction(ProtoId<NpcFactionPrototype> faction, bool isKeyPoint = true)
    {
        // Return the victory sound for the attacking faction
        var soundType = isKeyPoint ? "Key point" : "Secondary point";
        var soundPath = faction.Id switch
        {
            "NCR" => $"/Audio/_native-fallout/Effects/Victory/NCR {soundType}.ogg",
            "BrotherhoodMidwest" => $"/Audio/_native-fallout/Effects/Victory/BOS {soundType}.ogg",
            "CaesarLegion" => $"/Audio/_native-fallout/Effects/Victory/Legion {soundType}.ogg",
            "Tribal" => $"/Audio/_native-fallout/Effects/Victory/Tribe {soundType}.ogg",
            _ => null
        };
        
        if (soundPath != null)
            return new SoundPathSpecifier(soundPath);
        
        return null;
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
        _appearance.SetData(uid, PointOfInterestVisuals.AnimateFlag, poi.AnimateFlag);
        
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
