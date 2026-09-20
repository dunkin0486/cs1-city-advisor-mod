using System;
using System.Collections.Generic;
using System.Reflection;
using CitiesHarmony.API;
using ColossalFramework;
using ColossalFramework.Plugins;
using HarmonyLib;
using ICities;
using UnityEngine;

namespace CityAdvisor
{
    /// <summary>
    /// Compatibility helpers. Deliberately duplicated from the Traffic Flow
    /// Overlay mod rather than shared via a hard project reference — each
    /// mod should work standalone. If both are installed, pull in a small
    /// shared library later once the integration is actually built.
    /// </summary>
    public static class CompatUtil
    {
        /// <summary>
        /// Detects another mod by matching against loaded plugin assemblies,
        /// without a hard project reference to it.
        /// </summary>
        public static bool IsModEnabled(string assemblyNameContains)
        {
            foreach (PluginManager.PluginInfo plugin in PluginManager.instance.GetPluginsInfo())
            {
                if (!plugin.isEnabled) continue;
                foreach (Assembly asm in plugin.GetAssemblies())
                {
                    if (asm.GetName().Name.IndexOf(assemblyNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Reflection-based access to another mod's public data, used to
        /// optionally read the Traffic Flow Overlay mod's per-segment
        /// FlowRatio array if it's installed, without referencing its
        /// assembly directly. Returns null if unavailable for any reason
        /// (not installed, API changed, etc.) — callers must treat that as
        /// "fall back to density-only", never as an error.
        /// </summary>
        public static float[] TryGetOverlayFlowRatios()
        {
            try
            {
                if (!IsModEnabled("TrafficFlowOverlay"))
                {
                    return null;
                }

                // TODO: once TrafficFlowOverlay's SegmentFlowSampler is
                // stable, resolve the live instance and pull FlowRatio via
                // reflection here. Left unimplemented deliberately — this
                // should never be a hard requirement for CityAdvisor to
                // function. NOTE this is a live, current-impact gap, not
                // just a future nice-to-have: since this always returns
                // null, EVERY installation (with or without the overlay
                // mod) runs density-only classification today, and
                // SPEC.md's own reasoning for wanting flow-ratio gating
                // ("a busy-but-healthy road shouldn't fire this") is
                // unmitigated for everyone until this is implemented.
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CityAdvisor] Optional TrafficFlowOverlay integration failed, continuing without it: {e}");
                return null;
            }
        }
    }

    /// <summary>
    /// Mod entry point.
    /// </summary>
    public class CityAdvisorMod : IUserMod
    {
        // Not part of the ICities.IUserMod interface (confirmed via ILSpy
        // it only declares Name/Description on this game version) -- the
        // game's PluginManager looks these up by reflection
        // (GetMethod("OnEnabled"/"OnDisabled", ...)) and invokes them with
        // no arguments if present, confirmed via ILSpy against
        // ColossalManaged.dll. Public, no-arg, void is required for that
        // lookup to find them.
        private const string HarmonyId = "com.dunkin0486.cityadvisor";

        // Guards against two lifecycle races found in code review:
        // OnEnabled defers PatchAll asynchronously (DoOnHarmonyReady may
        // queue the action rather than run it immediately), while
        // OnDisabled's UnpatchAll runs synchronously -- without _enabled,
        // a disable-then-fast-re-enable could let a stale queued callback
        // patch an already-"disabled" mod. _patched prevents a second
        // queued callback from patching (and registering everything)
        // twice. Both are only touched from OnEnabled/OnDisabled/the
        // DoOnHarmonyReady callback, all on the main thread, so no
        // additional locking is needed.
        private static bool _enabled;
        private static bool _patched;

        public string Name => "City Advisor";

        public string Description =>
            "Diagnoses root causes behind city problems (e.g. missing highway " +
            "interchanges) instead of generic 'traffic flow is low' messages.";

        public void OnEnabled()
        {
            _enabled = true;
            Debug.Log("[CityAdvisor] OnEnabled called, waiting on Harmony...");
            HarmonyHelper.DoOnHarmonyReady(() =>
            {
                if (!_enabled || _patched)
                {
                    return;
                }

                var harmony = new Harmony(HarmonyId);
                // Each patch class is applied independently (rather than
                // one PatchAll(assembly) call) so a failure patching one
                // -- e.g. a future game update renaming a field
                // ChirpClickPatch relies on -- can't also silently
                // prevent ChirpClickCameraBoundsPatch, which patches an
                // unrelated method, from being applied too.
                PatchClass(harmony, typeof(ChirpClickPatch));
                PatchClass(harmony, typeof(ChirpClickCameraBoundsPatch));
                _patched = true;
            });
        }

        public void OnDisabled()
        {
            _enabled = false;
            if (_patched && HarmonyHelper.IsHarmonyInstalled)
            {
                new Harmony(HarmonyId).UnpatchAll(HarmonyId);
            }
            _patched = false;
        }

        private static void PatchClass(Harmony harmony, Type patchClass)
        {
            try
            {
                harmony.CreateClassProcessor(patchClass).Patch();
                Debug.Log($"[CityAdvisor] Harmony patch applied: {patchClass.Name}");
            }
            catch (Exception e)
            {
                // A failure here must not be silent, and must not look
                // like it broke the whole mod -- diagnostics, Chirper
                // messages, and text location hints all work
                // independently of any single Harmony patch.
                Debug.LogError($"[CityAdvisor] Harmony patch failed for {patchClass.Name}, " +
                                $"that feature will not work: {e}");
            }
        }
    }

    public class LoadingExtension : LoadingExtensionBase
    {
        private GameObject _schedulerObject;

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);

            if (mode != LoadMode.LoadGame && mode != LoadMode.NewGame)
            {
                return;
            }

            _schedulerObject = new GameObject("CityAdvisor_Scheduler");
            _schedulerObject.AddComponent<DiagnosticScheduler>();
        }

        public override void OnLevelUnloading()
        {
            if (_schedulerObject != null)
            {
                UnityEngine.Object.Destroy(_schedulerObject);
                _schedulerObject = null;
            }

            base.OnLevelUnloading();
        }
    }

    /// <summary>
    /// A single detected problem, produced by a diagnostic pass.
    /// </summary>
    public struct Finding
    {
        public string Issue;          // e.g. "missing_interchange"
        public Vector3 Position;
        public float Severity;        // 0..1
        public string DetailMessage;  // human-readable, shown in notification

        // Empty (InstanceID.Empty) when no specific game object represents
        // this finding's location. When set, ChirpClickPatch uses it to
        // make clicking the chirp's sender jump the camera here — see
        // SPEC.md "Location hints and click-to-jump".
        public InstanceID TargetInstance;
    }

    /// <summary>
    /// Shared context passed to every diagnostic each pass, so diagnostics
    /// don't each re-fetch singletons/buffers independently.
    /// </summary>
    public class DiagnosticContext
    {
        public NetManager NetManager;
        public DistrictManager DistrictManager;
        public BuildingManager BuildingManager;
        // TODO: add a reference to the Traffic Flow Overlay mod's
        // per-segment FlowRatio array here, IF that mod is present —
        // soft dependency, check for it via reflection on the loaded
        // mod list rather than a hard assembly reference.
    }

    public interface IDiagnostic
    {
        string Name { get; }
        List<Finding> Run(DiagnosticContext ctx);
    }

    /// <summary>
    /// Runs registered diagnostics on a slow, in-game-time-based cadence
    /// (not every frame) and hands findings off for surfacing.
    /// </summary>
    public class DiagnosticScheduler : MonoBehaviour
    {
        private const double RunIntervalGameDays = 7.0; // TODO: tune

        // How often (real seconds) to check whether RunIntervalGameDays
        // has elapsed. Was previously an every-frame Update() check --
        // wasteful for a gate that only changes at day granularity, so
        // InvokeRepeating on a real-time interval achieves the same
        // result (checked promptly, not on a strict schedule players
        // would notice) for a small fraction of the calls.
        private const float CheckIntervalSeconds = 5f;

        private double _lastRunGameDay = -1;
        private readonly List<IDiagnostic> _diagnostics = new List<IDiagnostic>
        {
            new MissingInterchangeDiagnostic(),
        };

        // Findings still active as of the last pass, so a pass that finds
        // the exact same problem again doesn't re-fire a duplicate chirp.
        // Replaced wholesale each pass (not merged) so a finding that
        // disappears -- the player fixed it -- and later reappears -- a
        // regression -- is treated as new again, matching what SPEC.md
        // always intended ("don't re-fire ... if nothing has changed")
        // but an earlier, ever-growing HashSet never actually implemented.
        private HashSet<string> _previouslyActiveKeys = new HashSet<string>();

        private void Start()
        {
            InvokeRepeating(nameof(CheckAndRunDiagnostics), CheckIntervalSeconds, CheckIntervalSeconds);
        }

        private void CheckAndRunDiagnostics()
        {
            double currentGameDay = GetCurrentGameDay();
            if (_lastRunGameDay >= 0 && currentGameDay - _lastRunGameDay < RunIntervalGameDays)
            {
                return;
            }
            _lastRunGameDay = currentGameDay;

            RunAllDiagnostics();
        }

        private double GetCurrentGameDay()
        {
            // TODO: verify against SimulationManager's actual time API for
            // the installed version — this is a placeholder access pattern.
            var time = Singleton<SimulationManager>.instance.m_currentGameTime;
            return time.ToOADate(); // arbitrary monotonic day-ish value; refine
        }

        private void RunAllDiagnostics()
        {
            var ctx = new DiagnosticContext
            {
                NetManager = Singleton<NetManager>.instance,
                DistrictManager = Singleton<DistrictManager>.instance,
                BuildingManager = Singleton<BuildingManager>.instance,
            };

            var currentActiveKeys = new HashSet<string>();

            foreach (var diagnostic in _diagnostics)
            {
                List<Finding> findings;
                try
                {
                    findings = diagnostic.Run(ctx);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[CityAdvisor] Diagnostic '{diagnostic.Name}' threw: {e}");
                    continue;
                }

                foreach (var finding in findings)
                {
                    string key = BuildFindingKey(finding);
                    currentActiveKeys.Add(key);

                    if (_previouslyActiveKeys.Contains(key))
                    {
                        continue; // same ongoing problem as last pass
                    }

                    Surface(finding);
                }
            }

            _previouslyActiveKeys = currentActiveKeys;
        }

        // Prefers the specific game object a finding points at (exact,
        // stable across passes) over stringifying Position, which used
        // Vector3's default ToString() -- limited float precision that
        // could in principle collide for two distinct nearby findings.
        private static string BuildFindingKey(Finding finding)
        {
            if (!finding.TargetInstance.IsEmpty)
            {
                return $"{finding.Issue}:{finding.TargetInstance.Type}:{finding.TargetInstance.RawData}";
            }

            return $"{finding.Issue}:{finding.Position}";
        }

        private void Surface(Finding finding)
        {
            Debug.Log($"[CityAdvisor] {finding.Issue} (severity {finding.Severity:F2}) " +
                      $"at {finding.Position}: {finding.DetailMessage}");

            Singleton<MessageManager>.instance.QueueMessage(new FindingChirpMessage(finding));
        }
    }

    /// <summary>
    /// Adapts a Finding into the Chirper feed's message type. Confirmed via
    /// ILSpy against Assembly-CSharp/ICities.dll for the installed version:
    /// MessageManager.QueueMessage(MessageBase) is the entry point mods use
    /// to inject a custom chirp; MessageBase implements ICities'
    /// IChirperMessage and exposes senderName/text/senderID via the
    /// GetSenderName/GetText/GetSenderID overrides below. senderID 0 renders
    /// as a chirp with no clickable citizen target (ChirpPanel treats it as
    /// an invalid InstanceID and no-ops on click) — there's no "system"
    /// sender concept, so 0 is the correct choice here, not a placeholder.
    /// </summary>
    public class FindingChirpMessage : MessageBase
    {
        private readonly Finding _finding;

        public FindingChirpMessage(Finding finding)
        {
            _finding = finding;
        }

        public override string GetSenderName() => "City Advisor";

        public override string GetText() => _finding.DetailMessage;

        public override uint GetSenderID() => 0u;

        // Read by ChirpClickPatch (Harmony) to redirect the chirp's
        // click-to-jump target from vanilla's Citizen-only behavior to the
        // finding's actual location.
        public InstanceID TargetInstance => _finding.TargetInstance;
        public Vector3 Position => _finding.Position;
    }

    /// <summary>
    /// First diagnostic: flags high-density traffic clusters with no
    /// highway interchange nearby. See SPEC.md for the full algorithm.
    /// </summary>
    public class MissingInterchangeDiagnostic : IDiagnostic
    {
        public string Name => "MissingInterchange";

        private const float InterchangeSearchRadius = 1500f; // meters, TODO tune
        private const float DensityThreshold = 0.6f;          // 0..1, TODO tune

        public List<Finding> Run(DiagnosticContext ctx)
        {
            var findings = new List<Finding>();
            var segments = ctx.NetManager.m_segments.m_buffer;

            // Computed once per pass and shared by FindInterchangeNodes and
            // the scan below, instead of calling the virtual
            // NetAI.IsHighway() up to 3x per segment (once per attached
            // node in FindInterchangeNodes, once here) -- classification
            // can't change mid-pass since this runs synchronously on the
            // main thread with no segment creation/deletion in between.
            var isHighwayCache = new bool[segments.Length];
            for (int i = 0; i < segments.Length; i++)
            {
                isHighwayCache[i] = IsHighwaySegment(segments[i]);
            }

            var interchangeNodePositions = FindInterchangeNodes(ctx, segments, isHighwayCache);

            // Optional: if the Traffic Flow Overlay mod is installed, prefer
            // requiring low flow-ratio alongside high density to cut false
            // positives (a busy-but-healthy road shouldn't fire this).
            // Falls back to density-only automatically if unavailable.
            float[] flowRatios = CompatUtil.TryGetOverlayFlowRatios();

            for (int segId = 0; segId < segments.Length; segId++)
            {
                var segment = segments[segId];
                if ((segment.m_flags & NetSegment.Flags.Created) == 0)
                {
                    continue;
                }
                if (isHighwayCache[segId])
                {
                    continue; // only care about arterial/local feeder roads
                }

                float density = segment.m_trafficDensity / 100f;
                if (density < DensityThreshold)
                {
                    continue;
                }

                if (flowRatios != null && segId < flowRatios.Length)
                {
                    const float lowFlowThreshold = 0.5f; // TODO: tune, mirror overlay mod's threshold
                    if (flowRatios[segId] >= lowFlowThreshold)
                    {
                        continue; // busy but still moving fine — not a ramp problem
                    }
                }

                Vector3 segmentMid = GetSegmentMidpoint(ctx.NetManager, segment);
                float nearestRampDist = NearestDistance(segmentMid, interchangeNodePositions);

                if (nearestRampDist > InterchangeSearchRadius)
                {
                    // Text location hint (district name, or compass
                    // direction fallback) stays even with click-to-jump
                    // below -- not every player notices/uses the click
                    // target, and GetDistrictName returning null for
                    // district 0 (the "nothing painted here" sentinel, not
                    // an error) is a real, expected case, not defensive
                    // code for something that can't happen.
                    //
                    // GetDistrict's grid only covers vanilla's ~4915m
                    // range and silently clamps beyond it (confirmed live:
                    // every far-map finding was misreported as the same
                    // wrong district) -- don't even call it outside that
                    // range, since the result can't be trusted at all.
                    string districtName = null;
                    if (LocationDescription.IsWithinVanillaDistrictGrid(segmentMid.x, segmentMid.z))
                    {
                        byte districtId = ctx.DistrictManager.GetDistrict(segmentMid);
                        districtName = ctx.DistrictManager.GetDistrictName(districtId);
                    }
                    string locationHint = LocationDescription.DescribeLocation(
                        districtName, segmentMid.x, segmentMid.z);

                    findings.Add(new Finding
                    {
                        Issue = "missing_interchange",
                        Position = segmentMid,
                        Severity = MissingInterchangeScoring.CalculateSeverity(
                            density, nearestRampDist, InterchangeSearchRadius),
                        DetailMessage =
                            $"High traffic density {locationHint} with no highway " +
                            $"interchange within {InterchangeSearchRadius:F0}m " +
                            $"(nearest is {nearestRampDist:F0}m away).",
                        // Confirmed via ILSpy (InstanceManager.IsValid /
                        // FollowInstance) that NetSegment is a fully
                        // supported, followable instance type -- this makes
                        // ChirpClickPatch's camera jump work the same way
                        // vanilla's own instance-following does.
                        TargetInstance = new InstanceID { NetSegment = (ushort)segId },
                    });
                }
            }

            return findings;
        }

        private List<Vector3> FindInterchangeNodes(DiagnosticContext ctx, NetSegment[] segments, bool[] isHighwayCache)
        {
            var result = new List<Vector3>();
            var nodes = ctx.NetManager.m_nodes.m_buffer;

            for (int nodeId = 0; nodeId < nodes.Length; nodeId++)
            {
                var node = nodes[nodeId];
                if ((node.m_flags & NetNode.Flags.Created) == 0)
                {
                    continue;
                }

                bool touchesHighway = false;
                bool touchesNonHighway = false;

                // NetNode itself exposes CountSegments()/GetSegment(i) over
                // its 8-slot m_segment0..m_segment7 fields (confirmed via
                // ILSpy against Assembly-CSharp for the installed version)
                // — use the game's own accessor rather than the raw fields
                // so this keeps working if the slot count ever changes.
                int segmentCount = node.CountSegments();
                for (int i = 0; i < segmentCount; i++)
                {
                    ushort segId = node.GetSegment(i);
                    if (segId == 0) continue;
                    if (isHighwayCache[segId]) touchesHighway = true;
                    else touchesNonHighway = true;
                }

                if (touchesHighway && touchesNonHighway)
                {
                    result.Add(node.m_position);
                }
            }

            return result;
        }

        private bool IsHighwaySegment(NetSegment segment)
        {
            NetInfo info = segment.Info;
            if (info == null || info.m_netAI == null) return false;

            // Confirmed via ILSpy against Assembly-CSharp for the installed
            // version: there is no ItemClass.SubService.Highway value (the
            // SPEC.md assumption was wrong) — vanilla instead exposes a
            // virtual NetAI.IsHighway() (default false), overridden by
            // RoadBaseAI to return its m_highwayRules field. This is the
            // exact check the game itself uses (e.g. for highway-specific
            // rendering and traffic rules), so any road pack that wants
            // highway behavior already has to set m_highwayRules — no
            // name-matching fallback needed.
            return info.m_netAI.IsHighway();
        }

        private Vector3 GetSegmentMidpoint(NetManager netManager, NetSegment segment)
        {
            var startNode = netManager.m_nodes.m_buffer[segment.m_startNode];
            var endNode = netManager.m_nodes.m_buffer[segment.m_endNode];
            return (startNode.m_position + endNode.m_position) * 0.5f;
        }

        private float NearestDistance(Vector3 point, List<Vector3> candidates)
        {
            float best = float.MaxValue;
            foreach (var c in candidates)
            {
                float d = Vector3.Distance(point, c);
                if (d < best) best = d;
            }
            return best;
        }
    }
}
