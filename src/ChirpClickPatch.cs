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
    /// Bypasses vanilla's owned-area camera clamp for our own click
    /// targets only.
    ///
    /// Root cause, found via a live-logged Prefix+Postfix directly on
    /// CameraController.SetTarget (see git history for the debug patches
    /// used to find this): SetTarget was being called correctly with a
    /// fully valid NetSegment InstanceID, genuinely different from the
    /// current target -- but immediately reverted m_targetInstance back to
    /// empty and m_targetPosition back to wherever the camera already was.
    /// That's SetTarget's own ClampPoint branch: it clamps the target
    /// position into GameAreaManager's recognized "owned" tile grid, and
    /// if the clamp changes the position, clears the target entirely. Our
    /// findings sit on tiles the 81 Tiles mod lets you build on, but that
    /// vanilla's own camera-bounds check apparently doesn't recognize as
    /// owned -- a partial-compatibility gap between 81 Tiles and vanilla's
    /// camera system, not a bug in our redirect (which was independently
    /// confirmed correct: valid, followable, genuinely-changed target).
    ///
    /// CameraController.m_unlimitedCamera skips that clamp entirely when
    /// true. Both it and ToolsModifierControl.cameraController are public
    /// fields (confirmed via ILSpy), so this needs no Harmony field
    /// injection -- just toggle it on immediately before vanilla's
    /// OnTargetClick runs (Prefix) and restore its original value right
    /// after (Postfix), scoped to only our own message's click so vanilla
    /// navigation elsewhere keeps its normal bounds behavior.
    /// </summary>
    [HarmonyPatch(typeof(ChirpPanel), "OnTargetClick")]
    public static class ChirpClickCameraBoundsPatch
    {
        private static bool _previousUnlimitedCamera;
        private static bool _shouldRestore;

        internal static void Prefix(UIComponent comp, UIMouseEventParameter p)
        {
            _shouldRestore = false;

            if (!(p?.source?.objectUserData is InstanceID id) || id.Type != InstanceType.NetSegment)
            {
                return;
            }

            CameraController controller = ToolsModifierControl.cameraController;
            if (controller == null)
            {
                return;
            }

            _previousUnlimitedCamera = controller.m_unlimitedCamera;
            controller.m_unlimitedCamera = true;
            _shouldRestore = true;
        }

        internal static void Postfix()
        {
            if (!_shouldRestore)
            {
                return;
            }

            CameraController controller = ToolsModifierControl.cameraController;
            if (controller != null)
            {
                controller.m_unlimitedCamera = _previousUnlimitedCamera;
            }
            _shouldRestore = false;
        }
    }
}
