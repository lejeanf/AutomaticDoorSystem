#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem.Editor
{
    /// <summary>
    /// Edit-mode stand-in for the player: the Scene-view camera, evaluated with the runtime's own
    /// side math (DoorSideMath via DoorAuthoring). Stand where the player would and the inspector
    /// tells you which side the runtime would read and which way the door would swing.
    /// </summary>
    public static class DoorSideProbe
    {
        public struct Reading
        {
            public Vector3 worldPosition;
            public bool isFront;
            public float frontDepth;

            public string SideName => isFront ? "FRONT" : "BACK";
        }

        public static Reading Evaluate(DoorAuthoring door, Vector3 worldPoint)
        {
            var direction = door.DirectionForwardFor(worldPoint, out var depth);
            return new Reading
            {
                worldPosition = worldPoint,
                isFront = direction == 1,
                frontDepth = depth
            };
        }

        public static bool TryEvaluateSceneCamera(DoorAuthoring door, out Reading reading)
        {
            reading = default;
            var view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null) return false;
            reading = Evaluate(door, view.camera.transform.position);
            return true;
        }
    }
}
#endif
