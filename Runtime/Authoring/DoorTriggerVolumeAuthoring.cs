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
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(volumeCenter, volumeSize);

            Gizmos.color = Color.green;
        }

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
