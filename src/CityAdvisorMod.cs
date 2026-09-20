using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Plugins;
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
                // function.
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
        public string Name => "City Advisor";

        public string Description =>
            "Diagnoses root causes behind city problems (e.g. missing highway " +
            "interchanges) instead of generic 'traffic flow is low' messages.";
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

        private double _lastRunGameDay = -1;
        private readonly List<IDiagnostic> _diagnostics = new List<IDiagnostic>
        {
            new MissingInterchangeDiagnostic(),
        };

        // Findings we've already surfaced, so we don't spam the same
        // message every pass. Keyed loosely by issue+position for now.
        private readonly HashSet<string> _alreadyReported = new HashSet<string>();

        private void Update()
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
                    string key = $"{finding.Issue}:{finding.Position}";
                    if (_alreadyReported.Contains(key))
                    {
                        continue;
                    }
                    _alreadyReported.Add(key);

                    Surface(finding);
                }
            }
        }

        private void Surface(Finding finding)
        {
            // Milestone 1: console only, to validate diagnostic correctness
            // before wiring up the in-game notification feed.
            Debug.Log($"[CityAdvisor] {finding.Issue} (severity {finding.Severity:F2}) " +
                      $"at {finding.Position}: {finding.DetailMessage}");

            // TODO milestone 2: replace/augment with a ChirpAI / MessageManager
            // call so this shows up as an in-game notification. Needs the
            // exact API surface confirmed against the installed game version.
        }
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

            var interchangeNodePositions = FindInterchangeNodes(ctx);
            var segments = ctx.NetManager.m_segments.m_buffer;

            // Optional: if the Traffic Flow Overlay mod is installed, prefer
            // requiring low flow-ratio alongside high density to cut false
            // positives (a busy-but-healthy road shouldn't fire this).
            // Falls back to density-only automatically if unavailable.
            float[] flowRatios = CompatUtil.TryGetOverlayFlowRatios();

            for (ushort segId = 0; segId < segments.Length; segId++)
            {
                var segment = segments[segId];
                if ((segment.m_flags & NetSegment.Flags.Created) == 0)
                {
                    continue;
                }
                if (IsHighwaySegment(segment))
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

                Vector3 segmentMid = GetSegmentMidpoint(ctx.NetManager, segId);
                float nearestRampDist = NearestDistance(segmentMid, interchangeNodePositions);

                if (nearestRampDist > InterchangeSearchRadius)
                {
                    findings.Add(new Finding
                    {
                        Issue = "missing_interchange",
                        Position = segmentMid,
                        Severity = Mathf.Clamp01(density * (nearestRampDist / InterchangeSearchRadius)),
                        DetailMessage =
                            $"High traffic density with no highway interchange within " +
                            $"{InterchangeSearchRadius:F0}m (nearest is {nearestRampDist:F0}m away).",
                    });
                }
            }

            return findings;
        }

        private List<Vector3> FindInterchangeNodes(DiagnosticContext ctx)
        {
            var result = new List<Vector3>();
            var nodes = ctx.NetManager.m_nodes.m_buffer;
            var segments = ctx.NetManager.m_segments.m_buffer;

            for (ushort nodeId = 0; nodeId < nodes.Length; nodeId++)
            {
                var node = nodes[nodeId];
                if ((node.m_flags & NetNode.Flags.Created) == 0)
                {
                    continue;
                }

                bool touchesHighway = false;
                bool touchesNonHighway = false;

                // TODO: verify the segment-list field/accessor for nodes in
                // the installed version — historically an 8-slot array
                // (m_segment0..m_segment7) but this has been refactored
                // across patches. Replace with the correct iteration.
                foreach (ushort segId in GetNodeSegments(node))
                {
                    if (segId == 0) continue;
                    var seg = segments[segId];
                    if (IsHighwaySegment(seg)) touchesHighway = true;
                    else touchesNonHighway = true;
                }

                if (touchesHighway && touchesNonHighway)
                {
                    result.Add(node.m_position);
                }
            }

            return result;
        }

        private IEnumerable<ushort> GetNodeSegments(NetNode node)
        {
            throw new NotImplementedException(
                "Enumerate node's attached segment IDs — confirm field layout via ILSpy for installed version.");
        }

        private bool IsHighwaySegment(NetSegment segment)
        {
            NetInfo info = segment.Info;
            if (info == null) return false;

            // COMPATIBILITY NOTE: name-based matching is a real liability
            // with road packs (Network Extensions 2, CSUR, etc.) that don't
            // follow vanilla naming. Prefer info.m_class.m_subService ==
            // ItemClass.SubService.PublicTransportPlane-equivalent-for-
            // highways once confirmed against the decompiled enum for the
            // installed version — that's the mechanism roads themselves use
            // to self-declare as highway, so custom road packs that want
            // highway behavior already have to set it correctly. Treat the
            // string check below as a temporary fallback, not the long-term
            // approach.
            return info.name != null && info.name.IndexOf("Highway", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Vector3 GetSegmentMidpoint(NetManager netManager, ushort segId)
        {
            var segment = netManager.m_segments.m_buffer[segId];
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
