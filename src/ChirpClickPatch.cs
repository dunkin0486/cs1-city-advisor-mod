using ColossalFramework.UI;
using HarmonyLib;
using ICities;

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
                }

                break;
            }
        }
    }
}
