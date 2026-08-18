using UnityEngine;

namespace AutomaticDoorSystem
{
    /// <summary>
    /// Legacy companion object. Audio configuration now lives on <see cref="DoorAuthoring"/> in the
    /// subscene and is baked into the door entity, so nothing in the main scene needs to mirror it.
    /// <para>
    /// This component no longer does anything at runtime. It is kept only so existing scenes keep
    /// opening without missing-script errors, and so the Setup Validator
    /// (Tools &gt; AutomaticDoorSystem &gt; Setup Validator) can copy <see cref="audioConfiguration"/>
    /// onto the matching DoorAuthoring and then delete these objects. It will be removed in a
    /// future release.
    /// </para>
    /// <para>
    /// Deliberately NOT marked [Obsolete]: Unity draws its own deprecation help box from that
    /// attribute, stacked on top of the one DoorIdentifierEditor draws, and the pair said the
    /// same thing twice. The editor box carries the full migration steps and the validator
    /// button, so it is the one that stays.
    /// </para>
    /// </summary>
    [AddComponentMenu("")] // hidden from Add Component - nothing new should use it
    public class DoorIdentifier : MonoBehaviour
    {
        [Tooltip("The door number this object was associated with (matches DoorAuthoring.doorId)")]
        public int doorNumber;

        [Tooltip("Audio configuration that should be moved onto the matching DoorAuthoring")]
        public DoorAudioConfiguration audioConfiguration;
    }
}
