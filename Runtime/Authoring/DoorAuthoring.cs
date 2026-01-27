using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AutomaticDoorSystem
{
    public class DoorAuthoring : MonoBehaviour
    {
        [Header("Per-Instance Settings")]
        [Tooltip("REQUIRED: Unique identifier for this specific door instance (used for lock/unlock events)")]
        public int doorId = 0;

        [Header("Door Mesh References")]
        [Tooltip("The actual door mesh GameObject to animate (for single doors)")]
        public Transform doorMesh;

        [Tooltip("For double doors: Left and Right door meshes")]
        public Transform leftDoorMesh;
        public Transform rightDoorMesh;

        [Header("Trigger Volume")]
        [Tooltip("Child GameObject containing the trigger volume (should have DoorTriggerVolumeAuthoring component)")]
        public Transform triggerVolumeObject;

        [Header("Debug Settings")]
        [Tooltip("Enable debug visualization in scene view for this specific door instance")]
        public bool enableDebug = false;
        
        [Tooltip("REQUIRED: Reference to the shared DoorConfig asset that defines this door's behavior")]
        public DoorConfig doorConfig;

        private void OnDrawGizmosSelected()
        {
            if (doorConfig == null)
            {
#if UNITY_EDITOR
                var errorPos = transform.position + Vector3.up * 2f;
                UnityEditor.Handles.Label(errorPos,
                    "ERROR: DoorConfig is NULL!\nAssign a DoorConfig asset to this door.",
                    new UnityEngine.GUIStyle()
                    {
                        normal = new UnityEngine.GUIStyleState() { textColor = Color.red, background = MakeTex(2, 2, new Color(0.5f, 0, 0, 0.9f)) },
                        fontSize = 12,
                        fontStyle = FontStyle.Bold,
                        padding = new UnityEngine.RectOffset(8, 8, 5, 5)
                    });
#endif
                return;
            }

            Gizmos.matrix = Matrix4x4.identity;

            var isDouble = doorConfig.doorCount == DoorConfig.DoorCountEnum.Double;
            var isRotating = doorConfig.doorMovement == DoorConfig.DoorMovementEnum.Rotating;

            if (isRotating)
            {
                DrawRotatingDoorGizmos(isDouble);
            }
            else
            {
                DrawSlidingDoorGizmos(isDouble);
            }

            DrawTriggerVolumeGizmo();

            if (enableDebug)
            {
                DrawDebugInfo();
            }
        }

        private void DrawTriggerVolumeGizmo()
        {
            if (triggerVolumeObject != null)
            {
                var volumeAuthoring = triggerVolumeObject.GetComponent<DoorTriggerVolumeAuthoring>();
                if (volumeAuthoring != null)
                {
                    Vector3 triggerSize = volumeAuthoring.volumeSize;
                    Vector3 triggerCenter = volumeAuthoring.volumeCenter;

                    Gizmos.color = enableDebug ? new Color(0f, 1f, 0f, 0.3f) : new Color(1f, 1f, 0f, 0.2f);
                    Gizmos.matrix = triggerVolumeObject.localToWorldMatrix;
                    Gizmos.DrawCube(triggerCenter, triggerSize);

                    Gizmos.color = enableDebug ? Color.green : Color.yellow;
                    Gizmos.DrawWireCube(triggerCenter, triggerSize);
                    Gizmos.matrix = Matrix4x4.identity;
                }
            }
        }

        private void DrawDebugInfo()
        {
#if UNITY_EDITOR
            if (doorConfig == null) return;

            if (doorConfig.doorMovement == DoorConfig.DoorMovementEnum.Sliding)
            {
                if (doorConfig.doorCount == DoorConfig.DoorCountEnum.Single && doorMesh != null)
                {
                    var initialPos = doorMesh.position;
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(initialPos, 0.15f);
                    UnityEditor.Handles.Label(initialPos + Vector3.up * 0.3f, "Closed Position",
                        new UnityEngine.GUIStyle() { normal = new UnityEngine.GUIStyleState() { textColor = Color.red } });

                    var targetPos = initialPos + doorConfig.slideOpenOffset;
                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere(targetPos, 0.15f);
                    UnityEditor.Handles.Label(targetPos + Vector3.up * 0.3f, "Open Position",
                        new UnityEngine.GUIStyle() { normal = new UnityEngine.GUIStyleState() { textColor = Color.green } });

                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(initialPos, targetPos);
                }
                else if (doorConfig.doorCount == DoorConfig.DoorCountEnum.Double)
                {
                    if (leftDoorMesh != null)
                    {
                        var leftInitialPos = leftDoorMesh.position;
                        var leftTargetPos = leftInitialPos + doorConfig.slideOpenOffset;

                        Gizmos.color = Color.red;
                        Gizmos.DrawWireSphere(leftInitialPos, 0.12f);
                        Gizmos.color = Color.green;
                        Gizmos.DrawWireSphere(leftTargetPos, 0.12f);
                        Gizmos.color = Color.cyan;
                        Gizmos.DrawLine(leftInitialPos, leftTargetPos);
                    }

                    if (rightDoorMesh != null)
                    {
                        var rightInitialPos = rightDoorMesh.position;
                        var rightTargetPos = rightInitialPos + new Vector3(-doorConfig.slideOpenOffset.x, doorConfig.slideOpenOffset.y, doorConfig.slideOpenOffset.z);

                        Gizmos.color = Color.red;
                        Gizmos.DrawWireSphere(rightInitialPos, 0.12f);
                        Gizmos.color = Color.green;
                        Gizmos.DrawWireSphere(rightTargetPos, 0.12f);
                        Gizmos.color = Color.cyan;
                        Gizmos.DrawLine(rightInitialPos, rightTargetPos);
                    }
                }

                var doorRoot = transform.position + Vector3.up * 2.5f;
                UnityEditor.Handles.Label(doorRoot,
                    $"Door ID: {doorId}\n" +
                    $"Config: {doorConfig.name}\n" +
                    $"Type: {doorConfig.doorCount} {doorConfig.doorMovement}\n" +
                    $"Slide Offset: {doorConfig.slideOpenOffset}\n" +
                    $"Animation: {doorConfig.animationDuration}s\n" +
                    $"Auto-Close: {doorConfig.autoCloseDelay}s\n" +
                    $"DEBUG ENABLED",
                    new UnityEngine.GUIStyle()
                    {
                        normal = new UnityEngine.GUIStyleState() { textColor = Color.white, background = MakeTex(2, 2, new Color(0, 0, 0, 0.7f)) },
                        fontSize = 11,
                        padding = new UnityEngine.RectOffset(5, 5, 3, 3)
                    });
            }
#endif
        }

#if UNITY_EDITOR
        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
#endif

        private void DrawRotatingDoorGizmos(bool isDouble)
        {
            if (doorConfig == null) return;

            if (isDouble)
            {
                var openingStyle = doorConfig.openingStyle;

                switch (openingStyle)
                {
                    case DoorConfig.OpeningStyle.Forward:
                        if (leftDoorMesh != null)
                        {
                            DrawRotationArcForDoubleDoor(leftDoorMesh.position, leftDoorMesh.rotation, doorConfig.openForwardAngle, Color.green, "Forward L", true);
                            DrawRotationArcForDoubleDoor(leftDoorMesh.position, leftDoorMesh.rotation, doorConfig.openBackwardAngle, Color.red, "Backward L", true);
                        }
                        if (rightDoorMesh != null)
                        {
                            DrawRotationArcForDoubleDoor(rightDoorMesh.position, rightDoorMesh.rotation, doorConfig.openForwardAngle, Color.green, "Forward R", false);
                            DrawRotationArcForDoubleDoor(rightDoorMesh.position, rightDoorMesh.rotation, doorConfig.openBackwardAngle, Color.red, "Backward R", false);
                        }
                        break;

                    case DoorConfig.OpeningStyle.BothWay:
                        if (leftDoorMesh != null)
                        {
                            DrawRotationArcForDoubleDoor(leftDoorMesh.position, leftDoorMesh.rotation, doorConfig.openForwardAngle, Color.cyan, "Forward L", true);
                        }
                        if (rightDoorMesh != null)
                        {
                            DrawRotationArcForDoubleDoor(rightDoorMesh.position, rightDoorMesh.rotation, doorConfig.openForwardAngle, Color.cyan, "Forward R", false);
                        }
                        break;

                    case DoorConfig.OpeningStyle.OneWay:
                        bool useForward = doorConfig.oneWayDirection.z >= 0;
                        float angle = useForward ? doorConfig.openBackwardAngle : doorConfig.openForwardAngle;
                        Color arcColor = useForward ? new Color(1f, 0.5f, 0f) : Color.magenta; // Orange for backward, magenta for forward
                        string directionLabel = useForward ? "Backward (OneWay)" : "Forward (OneWay)";

                        if (leftDoorMesh != null)
                        {
                            DrawRotationArcForDoubleDoor(leftDoorMesh.position, leftDoorMesh.rotation, angle, arcColor, directionLabel + " L", true);
                        }
                        if (rightDoorMesh != null)
                        {
                            DrawRotationArcForDoubleDoor(rightDoorMesh.position, rightDoorMesh.rotation, angle, arcColor, directionLabel + " R", false);
                        }
                        break;
                }
            }
            else
            {
                var openingStyle = doorConfig.openingStyle;
                Vector3 doorPosition = doorMesh != null ? doorMesh.position : transform.position;
                Quaternion doorRotation = doorMesh != null ? doorMesh.rotation : transform.rotation;

                switch (openingStyle)
                {
                    case DoorConfig.OpeningStyle.Forward:
                        DrawRotationArc(doorPosition, doorRotation, doorConfig.openForwardAngle, Color.green, "Forward");
                        DrawRotationArc(doorPosition, doorRotation, doorConfig.openBackwardAngle, Color.red, "Backward");
                        break;

                    case DoorConfig.OpeningStyle.OneWay:
                        bool useForward = doorConfig.oneWayDirection.z >= 0;
                        float angle = useForward ? doorConfig.openBackwardAngle : doorConfig.openForwardAngle;
                        Color arcColor = useForward ? new Color(1f, 0.5f, 0f) : Color.magenta; // Orange for backward, magenta for forward
                        string directionLabel = useForward ? "Backward (OneWay)" : "Forward (OneWay)";
                        DrawRotationArc(doorPosition, doorRotation, angle, arcColor, directionLabel);
                        break;

                    case DoorConfig.OpeningStyle.BothWay:
                        DrawRotationArc(doorPosition, doorRotation, doorConfig.openForwardAngle, Color.green, "Forward");
                        DrawRotationArc(doorPosition, doorRotation, doorConfig.openBackwardAngle, Color.red, "Backward");
                        break;
                }
            }
        }

        private void DrawRotationArcForDoubleDoor(Vector3 position, Quaternion rotation, float angle, Color color, string label, bool isLeftDoor)
        {
            Gizmos.color = color;

            float arcRadius = 1.5f; 
            int segments = 20;

            float actualAngle = isLeftDoor ? -angle : angle;
            Vector3 startDirection = isLeftDoor ? Vector3.left : Vector3.right;

            for (int i = 0; i < segments; i++)
            {
                float currentAngle = (actualAngle / segments) * i;
                float nextAngle = (actualAngle / segments) * (i + 1);

                Vector3 currentPoint = position + rotation * Quaternion.Euler(0, currentAngle, 0) * startDirection * arcRadius;
                Vector3 nextPoint = position + rotation * Quaternion.Euler(0, nextAngle, 0) * startDirection * arcRadius;

                Gizmos.DrawLine(currentPoint, nextPoint);
            }

            Vector3 endPoint = position + rotation * Quaternion.Euler(0, actualAngle, 0) * startDirection * arcRadius;

            Gizmos.color = new Color(color.r, color.g, color.b, 0.7f);
            Gizmos.DrawLine(position, endPoint);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(endPoint, $"{label}\n{actualAngle:F0}°");
#endif
        }

        private void DrawRotationArc(Vector3 position, Quaternion rotation, float angle, Color color, string label)
        {
            Gizmos.color = color;
            Gizmos.color = new Color(color.r, color.g, color.b, 0.5f);
            float doorwayLineLength = 1f;
            Vector3 doorwayDir = rotation * Vector3.forward;
            Gizmos.DrawLine(position - doorwayDir * doorwayLineLength * 0.5f,
                           position + doorwayDir * doorwayLineLength * 0.5f);

            Gizmos.color = color;

            float arcRadius = 1.5f;
            int segments = 15;
            float angleStep = angle / segments;

            for (int i = 0; i < segments; i++)
            {
                float currentAngle = angleStep * i;
                float nextAngle = angleStep * (i + 1);

                Vector3 start = position + rotation * Quaternion.Euler(0, currentAngle, 0) * Vector3.right * arcRadius;
                Vector3 end = position + rotation * Quaternion.Euler(0, nextAngle, 0) * Vector3.right * arcRadius;

                Gizmos.DrawLine(start, end);
            }

            Vector3 rotatedDir = rotation * Quaternion.Euler(0, angle, 0) * Vector3.right;
            Vector3 arcEnd = position + rotatedDir * arcRadius;
            Gizmos.DrawLine(position, arcEnd);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(arcEnd, $"{label}\n{angle:F0}°");
#endif
        }

        private void DrawSlidingDoorGizmos(bool isDouble)
        {
            if (doorConfig == null) return;

            if (isDouble)
            {
                if (leftDoorMesh != null)
                {
                    DrawSlideArrow(leftDoorMesh.position, doorConfig.slideOpenOffset, Color.cyan);
                }
                if (rightDoorMesh != null)
                {
                    DrawSlideArrow(rightDoorMesh.position, new Vector3(-doorConfig.slideOpenOffset.x, doorConfig.slideOpenOffset.y, doorConfig.slideOpenOffset.z), Color.cyan);
                }
            }
            else
            {
                if (doorMesh != null)
                {
                    DrawSlideArrow(doorMesh.position, doorConfig.slideOpenOffset, Color.cyan);
                }
                else
                {
                    DrawSlideArrow(transform.position, doorConfig.slideOpenOffset, Color.cyan);
                }
            }
        }

        private void DrawSlideArrow(Vector3 startPos, Vector3 offset, Color color)
        {
            Gizmos.color = color;

            Vector3 endPos = startPos + offset;

            Gizmos.DrawLine(startPos, endPos);

            Vector3 direction = offset.normalized;
            float arrowHeadLength = 0.3f;
            float arrowHeadAngle = 25f;

            Vector3 right = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 + arrowHeadAngle, 0) * Vector3.forward;
            Vector3 left = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 - arrowHeadAngle, 0) * Vector3.forward;

            Gizmos.DrawLine(endPos, endPos + right * arrowHeadLength);
            Gizmos.DrawLine(endPos, endPos + left * arrowHeadLength);

#if UNITY_EDITOR
            float distance = offset.magnitude;
            UnityEditor.Handles.Label(endPos, $"Slide\n{distance:F2}m");
#endif
        }

        class DoorBaker : Baker<DoorAuthoring>
        {
            public override void Bake(DoorAuthoring authoring)
            {
                if (authoring.doorConfig == null)
                {
                    Debug.LogError($"[DoorBaker] Door '{authoring.gameObject.name}' is missing DoorConfig reference! Skipping baking.", authoring.gameObject);
                    return;
                }

                var entity = GetEntity(TransformUsageFlags.Dynamic);

                var doorId = authoring.doorId;
                var debug = authoring.enableDebug;

                var config = authoring.doorConfig;
                var doorType = ComputeDoorType(config.doorCount, config.doorMovement);
                var animDuration = config.animationDuration;
                var autoClose = config.autoCloseDelay;
                var layerMask = config.canOpenLayerMask.value;
                var locked = config.startLocked;

                Vector3 triggerSize;
                Vector3 triggerCenter;
                Transform triggerTransform;

                if (authoring.triggerVolumeObject != null)
                {
                    var volumeAuthoring = authoring.triggerVolumeObject.GetComponent<DoorTriggerVolumeAuthoring>();
                    if (volumeAuthoring != null)
                    {
                        triggerSize = volumeAuthoring.volumeSize;
                        triggerCenter = volumeAuthoring.volumeCenter;
                        triggerTransform = authoring.triggerVolumeObject;
                    }
                    else
                    {
                        Debug.LogWarning($"[DoorBaker] Door '{authoring.gameObject.name}' has triggerVolumeObject set but no DoorTriggerVolumeAuthoring component found. Using default trigger volume.", authoring.gameObject);
                        triggerSize = new Vector3(3f, 3f, 3f);
                        triggerCenter = Vector3.zero;
                        triggerTransform = authoring.triggerVolumeObject;
                    }
                }
                else
                {
                    Debug.LogWarning($"[DoorBaker] Door '{authoring.gameObject.name}' has no triggerVolumeObject assigned. Using default trigger volume at door root.", authoring.gameObject);
                    triggerSize = new Vector3(3f, 3f, 3f);
                    triggerCenter = Vector3.zero;
                    triggerTransform = authoring.transform;
                }

                var doorAxis = CalculateDoorAxis(authoring.transform);

                AddComponent(entity, new DoorComponent
                {
                    DoorId = doorId,
                    Type = doorType,
                    Axis = doorAxis,
                    AnimationDuration = animDuration,
                    AutoCloseDelay = autoClose
                });

                AddComponent(entity, new DoorStateComponent
                {
                    CurrentState = DoorState.Closed,
                    PreviousState = DoorState.Closed,
                    StateTimer = 0f,
                    EntitiesInTrigger = 0,
                    IsLocked = (byte)(locked ? 1 : 0),
                    ShouldPlayOpenSound = 0,
                    ShouldPlayCloseSound = 0,
                    DirectionForward = 1
                });

                var worldCenter = triggerTransform.TransformPoint(triggerCenter);
                var localCenterRelativeToRoot = authoring.transform.InverseTransformPoint(worldCenter);

                AddComponent(entity, new DoorTriggerVolume
                {
                    Size = triggerSize,
                    Center = localCenterRelativeToRoot,
                    LayerMask = layerMask
                });

                var transformData = CalculateTransformData(authoring, config);
                AddComponent(entity, transformData);

                if (debug)
                {
                    AddComponent<DoorDebugComponent>(entity);
                }

                if (doorType == DoorType.RotatingDouble || doorType == DoorType.SlidingDouble)
                {
                    var buffer = AddBuffer<DoubleDoorBuffer>(entity);

                    if (authoring.leftDoorMesh != null)
                    {
                        DependsOn(authoring.leftDoorMesh);

                        var leftEntity = GetEntity(authoring.leftDoorMesh, TransformUsageFlags.Dynamic);
                        var leftColliderData = ExtractColliderData(authoring.leftDoorMesh);
                        buffer.Add(new DoubleDoorBuffer
                        {
                            DoorEntity = leftEntity,
                            IsLeftDoor = 1,
                            ColliderSize = leftColliderData.size,
                            ColliderCenter = leftColliderData.center,
                            HasColliderData = leftColliderData.hasData
                        });

                    }

                    if (authoring.rightDoorMesh != null)
                    {
                        DependsOn(authoring.rightDoorMesh);

                        var rightEntity = GetEntity(authoring.rightDoorMesh, TransformUsageFlags.Dynamic);
                        var rightColliderData = ExtractColliderData(authoring.rightDoorMesh);
                        buffer.Add(new DoubleDoorBuffer
                        {
                            DoorEntity = rightEntity,
                            IsLeftDoor = 0,
                            ColliderSize = rightColliderData.size,
                            ColliderCenter = rightColliderData.center,
                            HasColliderData = rightColliderData.hasData
                        });
                    }
                }
                else if (authoring.doorMesh != null)
                {
                    DependsOn(authoring.doorMesh);
                    var doorMeshEntity = GetEntity(authoring.doorMesh, TransformUsageFlags.Dynamic);
                    var colliderData = ExtractColliderData(authoring.doorMesh);

                    var buffer = AddBuffer<DoubleDoorBuffer>(entity);
                    buffer.Add(new DoubleDoorBuffer
                    {
                        DoorEntity = doorMeshEntity,
                        IsLeftDoor = 0,
                        ColliderSize = colliderData.size,
                        ColliderCenter = colliderData.center,
                        HasColliderData = colliderData.hasData
                    });

                }
            }

            private DoorType ComputeDoorType(DoorConfig.DoorCountEnum count, DoorConfig.DoorMovementEnum movement)
            {
                if (count == DoorConfig.DoorCountEnum.Single && movement == DoorConfig.DoorMovementEnum.Rotating)
                    return DoorType.RotatingSingle;
                else if (count == DoorConfig.DoorCountEnum.Double && movement == DoorConfig.DoorMovementEnum.Rotating)
                    return DoorType.RotatingDouble;
                else if (count == DoorConfig.DoorCountEnum.Single && movement == DoorConfig.DoorMovementEnum.Sliding)
                    return DoorType.SlidingSingle;
                else
                    return DoorType.SlidingDouble;
            }

            private DoorAxis CalculateDoorAxis(Transform doorTransform)
            {
                var eulerY = doorTransform.eulerAngles.y;
                var normalizedAngle = ((eulerY % 360f) + 360f) % 360f; 

                if (normalizedAngle < 45f || normalizedAngle >= 315f)
                    return DoorAxis.Z;
                else if (normalizedAngle >= 45f && normalizedAngle < 135f)
                    return DoorAxis.X;
                else if (normalizedAngle >= 135f && normalizedAngle < 225f)
                    return DoorAxis.NegZ;
                else
                    return DoorAxis.NegX;
            }

            private (float3 size, float3 center, byte hasData) ExtractColliderData(Transform panelTransform)
            {
                // Look for BoxCollider on the panel or its children
                var boxCollider = panelTransform.GetComponent<BoxCollider>();
                if (boxCollider == null)
                {
                    boxCollider = panelTransform.GetComponentInChildren<BoxCollider>();
                }

                if (boxCollider != null)
                {
                    DependsOn(boxCollider);
                    return (
                        new float3(boxCollider.size.x, boxCollider.size.y, boxCollider.size.z),
                        new float3(boxCollider.center.x, boxCollider.center.y, boxCollider.center.z),
                        1
                    );
                }

                // No collider found - return defaults
                return (new float3(1f, 2.5f, 0.1f), new float3(0.5f, 1.25f, 0f), 0);
            }

            private DoorTransformData CalculateTransformData(DoorAuthoring authoring, DoorConfig config)
            {
                var data = new DoorTransformData();
                var doorType = ComputeDoorType(config.doorCount, config.doorMovement);

                if (doorType == DoorType.SlidingSingle || doorType == DoorType.SlidingDouble)
                {
                    var openOffset = config.slideOpenOffset;
                    var localOffset = authoring.transform.InverseTransformVector(openOffset);
                    data.SlideOffset = new float3(localOffset.x, localOffset.y, localOffset.z);
                    
                    if (doorType == DoorType.SlidingSingle && authoring.doorMesh != null)
                    {
                        data.InitialPosition = authoring.doorMesh.localPosition;
                    }
                    else
                    {
                        data.InitialPosition = float3.zero;
                    }

                    data.ClosedRotation = quaternion.identity;
                    data.OpenRotationForward = quaternion.identity;
                    data.OpenRotationBackward = quaternion.identity;
                }
                else
                {
                    data.ClosedRotation = quaternion.identity;
                    var forwardAngle = config.openForwardAngle;
                    data.OpenRotationForward = Quaternion.Euler(0f, forwardAngle, 0f);
                    
                    var backwardAngle = config.openBackwardAngle;
                    data.OpenRotationBackward = Quaternion.Euler(0f, backwardAngle, 0f);
                    data.OpeningStyle = (OpeningStyle)config.openingStyle;

                    var oneWayDir = config.oneWayDirection;
                    if (oneWayDir.sqrMagnitude > 0.0001f)
                    {
                        oneWayDir = oneWayDir.normalized;
                    }
                    else
                    {
                        oneWayDir = Vector3.forward;
                    }
                    data.OneWayDirection = new float3(oneWayDir.x, oneWayDir.y, oneWayDir.z);

                    data.SlideOffset = float3.zero;
                    data.InitialPosition = float3.zero;
                }

                return data;
            }
        }
    }
}
