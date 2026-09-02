using Unity.Entities;
using UnityEngine;

namespace AutomaticDoorSystem
{
    public class DoorTriggerVolumeAuthoring : MonoBehaviour
    {
        [Header("Trigger Volume Settings")]
        [Tooltip("Size of the trigger volume for door detection")]
        public Vector3 volumeSize = new Vector3(3f, 3f, 3f);

        [Tooltip("Center offset of trigger volume in local space")]
        public Vector3 volumeCenter = Vector3.zero;

        private void OnDrawGizmosSelected()
        {
            // The door may override the anchor; if it does, this centre is NOT where the pooled
            // AudioSource ends up, so it must not present itself as the audio anchor.
            var door = GetComponentInParent<DoorAuthoring>();
            DrawVolumeGizmos(transform, volumeCenter, volumeSize, true,
                door == null || door.audioAnchor == null);
        }

        /// <summary>
        /// Draws the trigger volume plus the three points that matter when placing a door:
        /// the centre (where the pooled AudioSource is parked) and the bottom/top centre of the
        /// volume, which show how far the detection box reaches below and above the doorway.
        /// Shared with <see cref="DoorAuthoring"/> so the markers also show when the door root is selected.
        /// </summary>
        /// <param name="centreIsAudioAnchor">
        /// False when the door supplies an explicit audioAnchor. The centre marker is then
        /// suppressed so only one "Audio anchor" gizmo exists in the scene - drawing both, labelled
        /// identically, made it impossible to tell which one the AudioSource actually used.
        /// </param>
        public static void DrawVolumeGizmos(Transform volumeTransform, Vector3 localCenter, Vector3 size,
            bool drawLabels, bool centreIsAudioAnchor = true)
        {
            if (volumeTransform == null) return;

            var previousMatrix = Gizmos.matrix;
            Gizmos.matrix = volumeTransform.localToWorldMatrix;

            Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
            Gizmos.DrawCube(localCenter, size);
            Gizmos.color = new Color(0f, 1f, 0f, 0.9f);
            Gizmos.DrawWireCube(localCenter, size);

            Gizmos.matrix = previousMatrix;

            var halfHeight = new Vector3(0f, size.y * 0.5f, 0f);
            var worldCenter = volumeTransform.TransformPoint(localCenter);
            var worldBottom = volumeTransform.TransformPoint(localCenter - halfHeight);
            var worldTop = volumeTransform.TransformPoint(localCenter + halfHeight);

            // Small markers: the top/bottom points are flat discs lying in the volume's top and
            // bottom faces, the audio anchor a small wire sphere. Scaled a little with the volume
            // but capped so they never dominate the doorway.
            float markerRadius = Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.025f, 0.03f, 0.08f);
            var volumeUp = volumeTransform.TransformDirection(Vector3.up).normalized;

            Gizmos.color = Color.yellow;
            if (centreIsAudioAnchor) Gizmos.DrawWireSphere(worldCenter, markerRadius);
            Gizmos.DrawLine(worldBottom, worldTop);

#if UNITY_EDITOR
            var bottomColor = new Color(1f, 0.5f, 0f);
            UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            UnityEditor.Handles.color = bottomColor;
            UnityEditor.Handles.DrawSolidDisc(worldBottom, volumeUp, markerRadius);
            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.DrawSolidDisc(worldTop, volumeUp, markerRadius);

            if (!drawLabels) return;

            UnityEditor.Handles.Label(worldTop + Vector3.up * markerRadius * 2f,
                $"Top center\n{worldTop.y:F2}m",
                LabelStyle(Color.cyan));

            UnityEditor.Handles.Label(worldBottom - Vector3.up * markerRadius * 2f,
                $"Bottom center\n{worldBottom.y:F2}m",
                LabelStyle(new Color(1f, 0.6f, 0.2f)));

            if (!centreIsAudioAnchor) return;

            UnityEditor.Handles.Label(worldCenter + Vector3.right * markerRadius * 2f,
                "Audio anchor",
                LabelStyle(Color.yellow));
#endif
        }

#if UNITY_EDITOR
        private static GUIStyle LabelStyle(Color color)
        {
            return new GUIStyle
            {
                normal = new GUIStyleState { textColor = color },
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft
            };
        }
#endif

        class Baker : Baker<DoorTriggerVolumeAuthoring>
        {
            public override void Bake(DoorTriggerVolumeAuthoring authoring)
            {
                // This component doesn't create its own entity
                // The trigger volume data will be read by the parent DoorAuthoring
                // This is just for visualization and data storage
            }
        }
    }
}
