using ColossalFramework.UI;
using HarmonyLib;
using ICities;
using UnityEngine;

namespace CityAdvisor
{
    /// <summary>
    /// Makes clicking a City Advisor chirp's sender name jump the camera to
    /// the finding's actual location, instead of vanilla's Citizen-only
    /// behavior.
    ///
    /// Confirmed via ILSpy: ChirpPanel.AddEntry hardcodes the click
    /// target's objectUserData to `new InstanceID { Citizen =
    /// message.senderID }`, and ChirpPanel.OnTargetClick only ever reads
    /// that -- there is no IChirperMessage hook for a different target
    /// type. Rather than reimplementing OnTargetClick's camera-jump logic
    /// (and risk drifting from vanilla behavior), this patches AddEntry as
    /// a Postfix and overwrites the button's objectUserData afterward, for
    /// our message type only. Vanilla's unmodified OnTargetClick then
    /// handles the actual click -- confirmed via ILSpy that
    /// InstanceManager.IsValid/FollowInstance already fully support
    /// InstanceType.NetSegment as a followable target, the same as any
    /// vanilla Citizen/Building/etc. target.
    /// </summary>
    [HarmonyPatch(typeof(ChirpPanel), "AddEntry")]
    public static class ChirpClickPatch
    {
        // Harmony matches this parameter by name against AddEntry's own
        // "message" parameter; "noAudio" is omitted since it's unused.
        // ___m_Container binds to ChirpPanel's private m_Container field
        // (confirmed via ILSpy: private UIScrollablePanel m_Container).
        internal static void Postfix(IChirperMessage message, UIScrollablePanel ___m_Container)
        {
            if (!(message is FindingChirpMessage finding) || ___m_Container == null)
            {
                return;
            }

            // AddEntry sets objectUserData = message on the panel it just
            // used (confirmed via ILSpy) -- reference-match against that
            // rather than assuming it's the last child, since AddEntry
            // recycles the oldest panel once the chirp buffer is full.
            foreach (UIComponent component in ___m_Container.components)
            {
                if (!ReferenceEquals(component.objectUserData, message))
                {
                    continue;
                }

                UIButton senderButton = component.Find<UIButton>("Sender");
                if (senderButton != null)
                {
                    senderButton.objectUserData = finding.TargetInstance;
                    Debug.Log("[CityAdvisor] ChirpClickPatch: redirected click target to " +
                              finding.TargetInstance.NetSegment);
                }
                else
                {
                    Debug.LogWarning("[CityAdvisor] ChirpClickPatch: found the chirp panel " +
                                      "but no \"Sender\" button inside it -- vanilla UI " +
                                      "template may have changed.");
                }

                return;
            }

            Debug.LogWarning("[CityAdvisor] ChirpClickPatch: could not find the panel " +
                              "AddEntry just created for this message -- click target not " +
                              "redirected, chirp will still show but won't be clickable.");
        }
    }

    /// <summary>
    /// Pure logging Prefix on OnTargetClick -- does not affect vanilla
    /// behavior (a void-returning Harmony Prefix always lets the original
    /// method run afterward). Added because a live test showed our
    /// redirected NetSegment InstanceID gets set correctly (confirmed via
    /// ChirpClickPatch's own log line) but clicking the chirp still does
    /// nothing, while an unrelated vanilla citizen chirp at the same
    /// far-map-edge location worked fine -- ruling out a general
    /// large-map/GameAreaManager clamping issue (also independently
    /// confirmed false via ILSpy: InstanceManager.IsValid/FollowInstance/
    /// GetPosition all fully support NetSegment). This logs exactly what
    /// OnTargetClick sees at the moment of the click, to find where in the
    /// remaining chain (objectUserData integrity, IsValid, or something in
    /// SetTarget/FollowTarget itself) this actually breaks.
    /// </summary>
    [HarmonyPatch(typeof(ChirpPanel), "OnTargetClick")]
    public static class ChirpClickDebugPatch
    {
        internal static void Prefix(UIComponent comp, UIMouseEventParameter p)
        {
            if (p?.source == null)
            {
                Debug.Log("[CityAdvisor] OnTargetClick: p.source is null, vanilla will no-op.");
                return;
            }

            object raw = p.source.objectUserData;
            if (raw is InstanceID id)
            {
                Debug.Log($"[CityAdvisor] OnTargetClick: objectUserData is InstanceID " +
                          $"type={id.Type} netSegment={id.NetSegment} " +
                          $"isEmpty={id.IsEmpty} isValid={InstanceManager.IsValid(id)}");
            }
            else
            {
                Debug.Log("[CityAdvisor] OnTargetClick: objectUserData is NOT an InstanceID, " +
                          $"actual type={raw?.GetType().FullName ?? "null"}");
            }
        }
    }

    /// <summary>
    /// Pure logging Prefix+Postfix on CameraController.SetTarget itself --
    /// added after ruling out every other layer: no other mod patches
    /// AddEntry/OnTargetClick (confirmed via Harmony.GetPatchInfo), and a
    /// live test showed OnTargetClick received a fully valid, followable
    /// NetSegment InstanceID for a genuine target change (not a same-
    /// target no-op) yet the camera didn't move at all. This directly
    /// observes whether SetTarget is even invoked with our data, and
    /// whether its private state (m_targetInstance/m_targetPosition/
    /// m_targetSize) actually changes afterward -- narrowing "SetTarget
    /// silently no-ops for this id" from "rendering doesn't reflect an
    /// applied state change" (two very different bugs to chase).
    /// ___ fields bind to CameraController's private fields by Harmony's
    /// naming convention.
    /// </summary>
    [HarmonyPatch(typeof(CameraController), "SetTarget")]
    public static class SetTargetDebugPatch
    {
        internal static void Prefix(InstanceID id, Vector3 position, bool zoomIn, InstanceID ___m_targetInstance)
        {
            Debug.Log($"[CityAdvisor] SetTarget CALLED: id.Type={id.Type} id.NetSegment={id.NetSegment} " +
                      $"position={position} zoomIn={zoomIn} currentTarget.Type={___m_targetInstance.Type} " +
                      $"sameAsCurrentTarget={id == ___m_targetInstance}");
        }

        internal static void Postfix(InstanceID ___m_targetInstance, Vector3 ___m_targetPosition, float ___m_targetSize)
        {
            Debug.Log($"[CityAdvisor] SetTarget AFTER: m_targetInstance.Type={___m_targetInstance.Type} " +
                      $"netSegment={___m_targetInstance.NetSegment} m_targetPosition={___m_targetPosition} " +
                      $"m_targetSize={___m_targetSize}");
        }
    }
}
