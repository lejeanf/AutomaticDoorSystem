using jeanf.validationTools;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace AutomaticDoorSystem
{
    public class DoorAuthoring : MonoBehaviour, IValidatable
    {
        [Tooltip("REQUIRED: Unique identifier for this specific door instance (used for lock/unlock events)")]
        public int doorId = 0;

        [Tooltip("Whether THIS door starts locked. Use Config takes the DoorConfig's Start Locked; Force On/Off " +
                 "override it here so a locked variant of a door no longer needs its own config asset.")]
        public ConfigBoolOverride startLockedOverride = ConfigBoolOverride.UseConfig;

        [Tooltip("Which side of THIS door counts as FRONT for Forward-style rotating doors. Use Config takes " +
                 "the DoorConfig's Invert Forward Side; Force On/Off override it for this door only - handy " +
                 "when one prefab sharing a config was modelled with its pivot the other way round, or one " +
                 "placed instance is mirrored. The FRONT/BACK gizmo and the side probe reflect the result.")]
        public ConfigBoolOverride invertForwardSideOverride = ConfigBoolOverride.UseConfig;

        /// <summary>Per-door override of a DoorConfig bool: keep the config's value, or force it.</summary>
        public enum ConfigBoolOverride
        {
            UseConfig = 0,
            ForceOff = 1,
            ForceOn = 2
        }

        [Tooltip("The actual door mesh GameObject to animate (for single doors)")]
        public Transform doorMesh;

        [Tooltip("For double doors: Left and Right door meshes")]
        public Transform leftDoorMesh;
        public Transform rightDoorMesh;

        [Tooltip("Child GameObject containing the trigger volume (should have DoorTriggerVolumeAuthoring component)")]
        public Transform triggerVolumeObject;

        [Header("Debug Settings")]
        [Tooltip("Enable debug visualization in scene view for this specific door instance")]
        public bool enableDebug = false;
        
        [Validation("A DoorConfig is required — baking SKIPS this door entirely without one.")]
        [Tooltip("REQUIRED: Reference to the shared DoorConfig asset that defines this door's behavior")]
        public DoorConfig doorConfig;

        [Tooltip("Audio settings for this door (open/close/lock clips + spatialization). " +
                 "Baked into the entity, so the pooled AudioSources pick it up without a companion object in the main scene. " +
                 "Leave empty for a silent door.")]
        public DoorAudioConfiguration doorAudioConfig;

        [Tooltip("Optional anchor for this door's sound. When assigned, the pooled AudioSource is placed " +
                 "at this transform instead of the trigger volume centre. Its position is baked, so move it " +
                 "in edit mode, not at runtime.")]
        public Transform audioAnchor;

        /// <summary>
        /// Panel wiring must match the assigned config: a Double config needs BOTH panels, a Single
        /// config needs the door mesh — otherwise nothing animates. Also invalid: a panel whose
        /// SteamAudioDynamicObject has no exported asset — the pooled panel proxy would then carry
        /// no Steam Audio geometry and the moving door silently stops occluding sound (select the
        /// panel and click 'Export Dynamic Object'). Surfaced through the propertyDrawer
        /// validation framework (inspector banner, hierarchy highlight, play-mode console scan).
        /// A null config returns true here because the [Validation] field above already flags it.
        /// Cross-scene rules (duplicate ids, managers present, subscene placement) stay in the
        /// DoorSetupValidatorWindow — a single component cannot see them.
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (HasUnexportedAudioGeometry(doorMesh) || HasUnexportedAudioGeometry(leftDoorMesh) ||
                    HasUnexportedAudioGeometry(rightDoorMesh)) return false;

                if (doorConfig == null) return true;
                return doorConfig.doorCount == DoorConfig.DoorCountEnum.Double
                    ? leftDoorMesh != null && rightDoorMesh != null
                    : doorMesh != null;
            }
        }

        private static bool HasUnexportedAudioGeometry(Transform panel)
        {
            return panel != null
                && panel.TryGetComponent<SteamAudio.SteamAudioDynamicObject>(out var dynamicObject)
                && dynamicObject.asset == null;
        }

        private static bool Resolve(ConfigBoolOverride overrideValue, bool configValue)
        {
            switch (overrideValue)
            {
                case ConfigBoolOverride.ForceOn: return true;
                case ConfigBoolOverride.ForceOff: return false;
                default: return configValue;
            }
        }

        /// <summary>What the baker bakes as InvertForwardSide: the config's flag unless overridden here.</summary>
        public bool EffectiveInvertForwardSide =>
            Resolve(invertForwardSideOverride, doorConfig != null && doorConfig.invertForwardSide);

        /// <summary>What the baker bakes as the initial lock state: the config's flag unless overridden here.</summary>
        public bool EffectiveStartLocked =>
            Resolve(startLockedOverride, doorConfig != null && doorConfig.startLocked);

        /// <summary>
        /// Root-local point the approach-side split plane passes through: the mean of the assigned
        /// panel pivots (hinges sit on the doorway; sliding panels sit in it). Falls back to the
        /// root origin when no panel is assigned. Baked into DoorComponent.SidePlaneLocalOrigin.
        /// </summary>
        public Vector3 SidePlaneLocalOrigin
        {
            get
            {
                var isDouble = doorConfig != null && doorConfig.doorCount == DoorConfig.DoorCountEnum.Double;
                var sum = Vector3.zero;
                var count = 0;

                if (isDouble)
                {
                    Accumulate(leftDoorMesh, ref sum, ref count);
                    Accumulate(rightDoorMesh, ref sum, ref count);
                }
                else
                {
                    Accumulate(doorMesh, ref sum, ref count);
                }

                return count == 0 ? Vector3.zero : sum / count;
            }
        }

        private void Accumulate(Transform panel, ref Vector3 sum, ref int count)
        {
            if (panel == null) return;
            sum += transform.InverseTransformPoint(panel.position);
            count++;
        }

        /// <summary>World position of <see cref="SidePlaneLocalOrigin"/>.</summary>
        public Vector3 SidePlaneWorldOrigin => transform.TransformPoint(SidePlaneLocalOrigin);

        /// <summary>
        /// World-space unit direction pointing into this door's FRONT half-space (local +Z, or -Z
        /// when inverted), i.e. the side the runtime reads as DirectionForward = 1.
        /// </summary>
        public Vector3 FrontDirectionWorld
        {
            get
            {
                var forward = transform.TransformDirection(Vector3.forward);
                if (forward.sqrMagnitude < 1e-8f) forward = Vector3.forward;
                forward.Normalize();
                return EffectiveInvertForwardSide ? -forward : forward;
            }
        }

        /// <summary>
        /// Runtime-identical approach-side test for a world point (see <see cref="DoorSideMath"/>).
        /// Returns 1 for FRONT, 0 for BACK, and how deep the point sits in front of the doorway plane
        /// in the root's local units (negative = behind).
        /// </summary>
        public byte DirectionForwardFor(Vector3 worldPoint, out float frontDepth)
        {
            var m = transform.worldToLocalMatrix;
            var worldToDoor = new float4x4(m.GetColumn(0), m.GetColumn(1), m.GetColumn(2), m.GetColumn(3));
            float3 origin = SidePlaneLocalOrigin;
            float3 point = worldPoint;
            frontDepth = DoorSideMath.FrontDepth(in worldToDoor, in origin, in point);
            return DoorSideMath.DirectionForward(frontDepth, EffectiveInvertForwardSide);
        }

        /// <summary>Runtime-identical trigger-volume test for a world point.</summary>
        public bool IsInsideTriggerVolume(Vector3 worldPoint)
        {
            var size = new Vector3(3f, 3f, 3f);
            var worldCenter = transform.position;
            if (triggerVolumeObject != null)
            {
                var volume = triggerVolumeObject.GetComponent<DoorTriggerVolumeAuthoring>();
                worldCenter = triggerVolumeObject.TransformPoint(volume != null ? volume.volumeCenter : Vector3.zero);
                if (volume != null) size = volume.volumeSize;
            }
            float3 point = worldPoint;
            float3 center = worldCenter;
            float3 volumeSize = size;
            return DoorSideMath.IsInsideVolume(in point, in center, in volumeSize);
        }

        /// <summary>
        /// Editor hook: returns a panel's authored (closed) world rotation while an edit-mode
        /// preview is posing it, so the swing-arc gizmos stay where the door path is instead of
        /// turning with the panel. Null = not previewing, use the live rotation.
        /// </summary>
        public static System.Func<Transform, Quaternion?> AuthoredWorldRotationProvider;

        /// <summary>Same for the authored (closed) world position, so slide arrows stay put while a panel is previewed.</summary>
        public static System.Func<Transform, Vector3?> AuthoredWorldPositionProvider;

        private static Quaternion ClosedWorldRotation(Transform panel)
        {
            var provided = AuthoredWorldRotationProvider?.Invoke(panel);
            return provided ?? panel.rotation;
        }

        private static Vector3 ClosedWorldPosition(Transform panel)
        {
            var provided = AuthoredWorldPositionProvider?.Invoke(panel);
            return provided ?? panel.position;
        }

        /// <summary>Scene camera side while gizmos are drawn; FRONT when there is no camera.</summary>
        private bool CameraIsOnFront()
        {
            var camera = Camera.current;
            return camera == null || DirectionForwardFor(camera.transform.position, out _) == 1;
        }

        /// <summary>
        /// True when the approach side decides the swing for this door: rotating, Forward style
        /// (or BothWay on a single, which the runtime treats as Forward).
        /// </summary>
        public bool ApproachSideMatters
        {
            get
            {
                if (doorConfig == null || doorConfig.doorMovement != DoorConfig.DoorMovementEnum.Rotating) return false;
                if (doorConfig.openingStyle == DoorConfig.OpeningStyle.Forward) return true;
                return doorConfig.openingStyle == DoorConfig.OpeningStyle.BothWay &&
                       doorConfig.doorCount == DoorConfig.DoorCountEnum.Single;
            }
        }

        /// <summary>
        /// World position the pooled AudioSource is placed at: the audioAnchor override when one is
        /// assigned, otherwise the centre of the trigger volume, which sits in the doorway rather
        /// than at the door's pivot (usually a hinge edge).
        /// Falls back to the door root when neither is assigned.
        /// </summary>
        public Vector3 GetAudioAnchorPosition()
        {
            if (audioAnchor != null) return audioAnchor.position;
            if (triggerVolumeObject == null) return transform.position;

            var volumeAuthoring = triggerVolumeObject.GetComponent<DoorTriggerVolumeAuthoring>();
            var localCenter = volumeAuthoring != null ? volumeAuthoring.volumeCenter : Vector3.zero;
            return triggerVolumeObject.TransformPoint(localCenter);
        }

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

            DrawTriggerVolumeGizmos();
            DrawAudioAnchorGizmo();
            if (ApproachSideMatters) DrawFrontSideGizmos();

            if (enableDebug)
            {
                DrawDebugInfo();
            }
        }

        /// <summary>
        /// Shows, before play mode, which side of the doorway the viewer is on: the split plane
        /// and an arrow into each half-space, the one the Scene camera is in drawn solid (green =
        /// front, red = back), the other faint. Same math as the runtime (full root matrix +
        /// panel-pivot plane + invert flag), so if the solid arrow is on the wrong side, flip
        /// Invert Forward Side. The inspector's Edit-Mode Door Test spells out the side in words.
        /// </summary>
        private void DrawFrontSideGizmos()
        {
            var origin = SidePlaneWorldOrigin;
            var front = FrontDirectionWorld;
            var right = transform.TransformDirection(Vector3.right);
            if (right.sqrMagnitude < 1e-8f) right = Vector3.right;
            right.Normalize();

            var isDouble = doorConfig.doorCount == DoorConfig.DoorCountEnum.Double;
            var doorwayWidth = isDouble
                ? GetDoorWidth(leftDoorMesh) + GetDoorWidth(rightDoorMesh)
                : GetDoorWidth(doorMesh);
            var halfWidth = Mathf.Max(doorwayWidth * 0.5f, 0.4f);
            var reach = Mathf.Clamp(halfWidth, 0.6f, 2f);

            var cameraOnFront = CameraIsOnFront();

            // The split plane, drawn as a line across the doorway at pivot height.
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            Gizmos.DrawLine(origin - right * halfWidth, origin + right * halfWidth);

            DrawSideArrow(origin, front * reach, FrontColor, cameraOnFront);
            DrawSideArrow(origin, -front * reach, BackColor, !cameraOnFront);

        }

        private void DrawSideArrow(Vector3 origin, Vector3 offset, Color color, bool highlighted)
        {
            var tip = origin + offset;
            Gizmos.color = highlighted ? color : new Color(color.r, color.g, color.b, 0.25f);
            Gizmos.DrawLine(origin, tip);
            var direction = offset.normalized;
            var headLength = Mathf.Min(0.25f, offset.magnitude * 0.3f);
            var headRight = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 + 25f, 0) * Vector3.forward;
            var headLeft = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 - 25f, 0) * Vector3.forward;
            Gizmos.DrawLine(tip, tip + headRight * headLength);
            Gizmos.DrawLine(tip, tip + headLeft * headLength);
            if (highlighted) Gizmos.DrawWireSphere(tip, headLength * 0.35f);
        }

        private void DrawAudioAnchorGizmo()
        {
            if (audioAnchor == null) return;

            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(audioAnchor.position, 0.15f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(audioAnchor.position + Vector3.up * 0.25f, "Audio Anchor");
#endif
        }

        private void DrawTriggerVolumeGizmos()
        {
            if (triggerVolumeObject == null) return;

            var volumeAuthoring = triggerVolumeObject.GetComponent<DoorTriggerVolumeAuthoring>();
            if (volumeAuthoring == null) return;

            DoorTriggerVolumeAuthoring.DrawVolumeGizmos(
                triggerVolumeObject, volumeAuthoring.volumeCenter, volumeAuthoring.volumeSize, true,
                audioAnchor == null);

            Gizmos.matrix = Matrix4x4.identity;
        }

        private void DrawDebugInfo()
        {
#if UNITY_EDITOR
            if (doorConfig == null) return;

            if (doorConfig.doorMovement == DoorConfig.DoorMovementEnum.Sliding)
            {
                if (doorConfig.doorCount == DoorConfig.DoorCountEnum.Single && doorMesh != null)
                {
                    var initialPos = ClosedWorldPosition(doorMesh);
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(initialPos, 0.15f);
                    UnityEditor.Handles.Label(initialPos + Vector3.up * 0.3f, "Closed Position",
                        new UnityEngine.GUIStyle() { normal = new UnityEngine.GUIStyleState() { textColor = Color.red } });

                    var targetPos = initialPos + SlideVectorToWorld(transform, doorConfig.slideOpenOffset);
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
                        var leftInitialPos = ClosedWorldPosition(leftDoorMesh);
                        var leftTargetPos = leftInitialPos + SlideVectorToWorld(transform, doorConfig.slideOpenOffset);

                        Gizmos.color = Color.red;
                        Gizmos.DrawWireSphere(leftInitialPos, 0.12f);
                        Gizmos.color = Color.green;
                        Gizmos.DrawWireSphere(leftTargetPos, 0.12f);
                        Gizmos.color = Color.cyan;
                        Gizmos.DrawLine(leftInitialPos, leftTargetPos);
                    }

                    if (rightDoorMesh != null)
                    {
                        var rightInitialPos = ClosedWorldPosition(rightDoorMesh);
                        var rightTargetPos = rightInitialPos +
                                             SlideVectorToWorld(transform, MirrorForRightPanel(doorConfig.slideOpenOffset));

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

        private static readonly Color FrontColor = Color.green;
        private static readonly Color BackColor = Color.red;

        /// <summary>
        /// The swing path of each panel, drawn from the CLOSED rotation so it stays fixed while
        /// the preview animates. The angle per panel is exactly what DoorAnimationSystem applies:
        ///   Forward  - single: +fwd from the front, bwd from the back; double: the left panel takes
        ///              bwd from the front / fwd from the back and the right panel the negated angle.
        ///   BothWay  - double: BOTH panels take the forward angle, un-mirrored (one swings each
        ///              way because the panels face each other); single: behaves as Forward.
        ///   OneWay   - bwd when One Way Direction z >= 0, else fwd; double right panel negated.
        /// Forward-style arcs show the swing for the Scene camera's side (green = front, red =
        /// back); OneWay is red (fixed swing), double BothWay cyan. Each panel's closed edge is
        /// read off its own geometry, not assumed from the left/right slot.
        /// </summary>
        private void DrawRotatingDoorGizmos(bool isDouble)
        {
            if (doorConfig == null) return;

            var style = doorConfig.openingStyle;
            var cameraOnFront = !ApproachSideMatters || CameraIsOnFront();
            var sideColor = cameraOnFront ? FrontColor : BackColor;
            var fwd = doorConfig.openForwardAngle;
            var bwd = doorConfig.openBackwardAngle;
            var oneWayAngle = doorConfig.oneWayDirection.z >= 0 ? bwd : fwd;

            if (isDouble)
            {
                float leftAngle, rightAngle;
                Color color;
                switch (style)
                {
                    case DoorConfig.OpeningStyle.BothWay:
                        leftAngle = rightAngle = fwd;
                        color = Color.cyan;
                        break;
                    case DoorConfig.OpeningStyle.OneWay:
                        leftAngle = oneWayAngle;
                        rightAngle = -oneWayAngle;
                        color = BackColor;
                        break;
                    default:
                        leftAngle = cameraOnFront ? bwd : fwd;
                        rightAngle = -leftAngle;
                        color = sideColor;
                        break;
                }
                DrawPanelSwing(leftDoorMesh, leftAngle, color);
                DrawPanelSwing(rightDoorMesh, rightAngle, color);
            }
            else
            {
                var angle = style == DoorConfig.OpeningStyle.OneWay ? oneWayAngle : (cameraOnFront ? fwd : bwd);
                var color = style == DoorConfig.OpeningStyle.OneWay ? BackColor : sideColor;
                DrawPanelSwing(doorMesh, angle, color);
            }
        }

        private void DrawPanelSwing(Transform panel, float localYAngle, Color color)
        {
            if (panel == null) return;
            DrawSwingArc(ClosedWorldPosition(panel), ClosedWorldRotation(panel), ClosedEdgeLocal(panel), localYAngle, color,
                GetDoorWidth(panel));
        }

        /// <summary>
        /// Which way the panel extends from its pivot along its own X, from the collider or
        /// renderer bounds: +X (Vector3.right) or -X (Vector3.left). Prefabs differ here, so the
        /// left/right slot must not be used to guess it.
        /// </summary>
        private static Vector3 ClosedEdgeLocal(Transform panel)
        {
            Vector3 center;
            var box = panel.GetComponent<BoxCollider>();
            if (box == null) box = panel.GetComponentInChildren<BoxCollider>();
            if (box != null)
            {
                center = box.transform.TransformPoint(box.center);
            }
            else
            {
                var renderer = panel.GetComponent<Renderer>();
                if (renderer == null) renderer = panel.GetComponentInChildren<Renderer>();
                if (renderer == null) return Vector3.right;
                center = renderer.bounds.center;
            }
            return Vector3.Dot(center - panel.position, panel.right) < 0f ? Vector3.left : Vector3.right;
        }

        private float GetDoorWidth(Transform doorTransform)
        {
            if (doorTransform == null) return 1.5f;

            // Try BoxCollider first (most accurate for door panels)
            var boxCollider = doorTransform.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = doorTransform.GetComponentInChildren<BoxCollider>();
            }

            if (boxCollider != null)
            {
                // Door width is typically local X, accounting for scale
                return boxCollider.size.x * doorTransform.lossyScale.x;
            }

            // Fallback to renderer bounds
            var renderer = doorTransform.GetComponent<Renderer>();
            if (renderer == null)
            {
                renderer = doorTransform.GetComponentInChildren<Renderer>();
            }

            if (renderer != null)
            {
                return renderer.bounds.size.x;
            }

            return 1.5f;
        }

        /// <summary>
        /// The path a panel sweeps on the ground: a translucent sector with a thick outline at
        /// the hinge pivot, from the panel's closed edge to its open edge, labelled with the
        /// angle. Drawn on top of geometry so the floor cannot hide it. Handles-based, editor only.
        /// </summary>
        private void DrawSwingArc(Vector3 pivot, Quaternion closedRotation, Vector3 closedEdgeLocal,
            float angle, Color color, float radius)
        {
#if UNITY_EDITOR
            var up = closedRotation * Vector3.up;
            var from = closedRotation * closedEdgeLocal;
            var center = pivot;
            var end = Quaternion.AngleAxis(angle, up) * from;

            UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            UnityEditor.Handles.color = new Color(color.r, color.g, color.b, 0.4f);
            UnityEditor.Handles.DrawSolidArc(center, up, from, angle, radius);
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.DrawWireArc(center, up, from, angle, radius, 4f);
            UnityEditor.Handles.DrawLine(center, center + end * radius, 4f);
            UnityEditor.Handles.DrawLine(center, center + from * radius, 1.5f);

            UnityEditor.Handles.Label(center + end * radius + up * 0.1f, $"{angle:F0}°",
                new GUIStyle
                {
                    normal = new GUIStyleState { textColor = color },
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                });
#endif
        }

        /// <summary>
        /// Rotates a door-local slide offset into world space for drawing. Uses TransformVector
        /// rather than TransformDirection so a scaled door root scales its travel, matching how
        /// the panels' local positions scale with it.
        /// </summary>
        public static Vector3 SlideVectorToWorld(Transform doorRoot, Vector3 localSlideOffset)
        {
            return doorRoot == null ? localSlideOffset : doorRoot.TransformVector(localSlideOffset);
        }

        /// <summary>
        /// The right panel of a Mirrored double slides the opposite way along the door's own X,
        /// which is the negation DoorAnimationSystem applies to SlideOffset.x at runtime.
        /// </summary>
        public static Vector3 MirrorForRightPanel(Vector3 localSlideOffset)
        {
            return new Vector3(-localSlideOffset.x, localSlideOffset.y, localSlideOffset.z);
        }

        private void DrawSlidingDoorGizmos(bool isDouble)
        {
            if (doorConfig == null) return;

            // The config offsets are door-local, so rotate them into world space to draw them.
            // This used to be TransformVector(InverseTransformVector(v)) - an exact no-op that
            // drew the raw world vector and so ignored the door's rotation entirely.
            Vector3 worldOffset = SlideVectorToWorld(transform, doorConfig.slideOpenOffset);

            if (isDouble && doorConfig.slidingStyle == DoorConfig.SlidingStyleEnum.Telescopic)
            {
                // Both panels travel the same direction; the right one leads and goes further.
                // With openRightDoorOnly the right arrow stops at the catch-up point (where the
                // left door sits) and the left door does not move at all.
                if (leftDoorMesh != null && !doorConfig.openRightDoorOnly)
                {
                    DrawSlideArrow(ClosedWorldPosition(leftDoorMesh), worldOffset, Color.cyan);
                }
                if (rightDoorMesh != null)
                {
                    var rightOffset = doorConfig.rightSlideOpenOffset;
                    if (doorConfig.openRightDoorOnly)
                    {
                        var rightSpan = rightOffset.magnitude;
                        var catchUp = Mathf.Max(rightSpan - doorConfig.slideOpenOffset.magnitude, 0f);
                        rightOffset = rightSpan > 1e-4f ? rightOffset / rightSpan * catchUp : Vector3.zero;
                    }
                    DrawSlideArrow(ClosedWorldPosition(rightDoorMesh), SlideVectorToWorld(transform, rightOffset), Color.cyan);
                }
            }
            else if (isDouble)
            {
                if (leftDoorMesh != null)
                {
                    DrawSlideArrow(ClosedWorldPosition(leftDoorMesh), worldOffset, Color.cyan);
                }
                if (rightDoorMesh != null)
                {
                    // Right door uses negated X in local space (matches animation system)
                    DrawSlideArrow(ClosedWorldPosition(rightDoorMesh),
                        SlideVectorToWorld(transform, MirrorForRightPanel(doorConfig.slideOpenOffset)),
                        Color.cyan);
                }
            }
            else
            {
                if (doorMesh != null)
                {
                    DrawSlideArrow(ClosedWorldPosition(doorMesh), worldOffset, Color.cyan);
                }
                else
                {
                    DrawSlideArrow(transform.position, worldOffset, Color.cyan);
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

                // Nearly everything below is read off this shared asset - door type, slide offsets,
                // sliding style, timings, layer mask, start-locked. Without this dependency Unity
                // never learns the entity is derived from it, so editing a DoorConfig left every
                // door baked with stale values: the gizmos (live authoring data) moved while the
                // baked entities did not, and only touching a door GameObject forced a rebake.
                DependsOn(authoring.doorConfig);

                var entity = GetEntity(TransformUsageFlags.Dynamic);

                var doorId = authoring.doorId;
                var debug = authoring.enableDebug;

                var config = authoring.doorConfig;
                var doorType = ComputeDoorType(config.doorCount, config.doorMovement);
                var animDuration = config.animationDuration;
                var autoClose = config.autoCloseDelay;
                var layerMask = config.canOpenLayerMask.value;
                var locked = authoring.EffectiveStartLocked;

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

                // Approach-side detection: the split plane goes through the panel pivots, in the
                // root's frame, so it follows the door's real rotation and scale (the old yaw-
                // quantized world axis misread any door turned off a cardinal angle or whose root
                // sits off the doorway). The panel transforms are declared as dependencies further
                // down, so moving a hinge rebakes this too.
                AddComponent(entity, new DoorComponent
                {
                    DoorId = doorId,
                    Type = doorType,
                    AnimationDuration = animDuration,
                    AutoCloseDelay = autoClose,
                    SidePlaneLocalOrigin = authoring.SidePlaneLocalOrigin,
                    InvertForwardSide = (byte)(authoring.EffectiveInvertForwardSide ? 1 : 0)
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

                // Baked even when null: the runtime bridge then knows this door is deliberately silent
                // instead of assuming the config just has not loaded yet.
                if (authoring.doorAudioConfig != null)
                {
                    DependsOn(authoring.doorAudioConfig);
                }

                float3 audioAnchorLocal;
                if (authoring.audioAnchor != null)
                {
                    DependsOn(authoring.audioAnchor);
                    audioAnchorLocal = authoring.transform.InverseTransformPoint(authoring.audioAnchor.position);
                }
                else
                {
                    audioAnchorLocal = localCenterRelativeToRoot;
                }

                AddComponent(entity, new DoorAudioConfigReference
                {
                    Config = authoring.doorAudioConfig,
                    AnchorLocalPosition = audioAnchorLocal
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
                            HasColliderData = leftColliderData.hasData,
                            InitialLocalPosition = authoring.leftDoorMesh.localPosition,
                            AudioGeometry = ExtractAudioGeometry(authoring.leftDoorMesh)
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
                            HasColliderData = rightColliderData.hasData,
                            InitialLocalPosition = authoring.rightDoorMesh.localPosition,
                            AudioGeometry = ExtractAudioGeometry(authoring.rightDoorMesh)
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
                        HasColliderData = colliderData.hasData,
                        InitialLocalPosition = authoring.doorMesh.localPosition,
                        AudioGeometry = ExtractAudioGeometry(authoring.doorMesh)
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

            /// <summary>
            /// Steam Audio geometry authored on the panel (SteamAudioDynamicObject + exported
            /// asset). The component itself is stripped at bake; only the asset reference
            /// survives, and the collider pool re-attaches it to the pooled panel proxy.
            /// </summary>
            private UnityObjectRef<SteamAudio.SerializedData> ExtractAudioGeometry(Transform panelTransform)
            {
                var dynamicObject = panelTransform.GetComponent<SteamAudio.SteamAudioDynamicObject>();
                if (dynamicObject == null) return default;

                DependsOn(dynamicObject);
                if (dynamicObject.asset == null)
                {
                    Debug.LogWarning($"[DoorAuthoring] '{panelTransform.name}' has a SteamAudioDynamicObject with no " +
                        "exported asset — the moving door will not occlude or reflect sound. Select the panel and " +
                        "click 'Export Dynamic Object', then re-bake.", panelTransform);
                    return default;
                }
                return dynamicObject.asset;
            }

            private (float3 size, float3 center, byte hasData) ExtractColliderData(Transform panelTransform)
            {
                var boxCollider = panelTransform.GetComponent<BoxCollider>();
                if (boxCollider == null)
                {
                    boxCollider = panelTransform.GetComponentInChildren<BoxCollider>();
                }

                if (boxCollider != null)
                {
                    DependsOn(boxCollider);
                    DependsOn(boxCollider.transform);

                    // The pooled proxy reproduces this box at the PANEL pivot's position+rotation,
                    // scale 1 - but door models author the BoxCollider on the mesh child under the
                    // animated CTRL node, whose local offset/rotation/scale a raw center/size copy
                    // silently drops (the collider then lands mirrored across the hinge at runtime,
                    // while edit mode - which uses the child's real transform - looks perfect).
                    // Fold the whole chain into the panel frame instead.
                    var worldSize = DoorColliderBake.WorldSize(boxCollider);
                    DoorColliderBake.DescribeInPanelFrame(
                        panelTransform.position, panelTransform.rotation,
                        DoorColliderBake.WorldCenter(boxCollider),
                        boxCollider.transform.rotation,
                        worldSize,
                        out var center, out var size, out var relativeAngle);

                    // Right-angle rotations reproduce exactly (ratio 1); only oblique ones bloat.
                    if (DoorColliderBake.BloatRatio(size, worldSize) > DoorColliderBake.BloatWarnRatio)
                    {
                        Debug.LogWarning(
                            $"[DoorBaker] Panel '{panelTransform.name}': its BoxCollider (on '{boxCollider.name}') is " +
                            $"rotated {relativeAngle:0}° relative to the panel pivot. The pooled collider can only be " +
                            "an axis-aligned box in the panel's frame, so the enclosing (larger) box is baked. Align " +
                            "the collider's node with the panel pivot for an exact fit.", boxCollider);
                    }

                    return (size, center, 1);
                }

                return (new float3(1f, 2.5f, 0.1f), new float3(0.5f, 1.25f, 0f), 0);
            }

            private DoorTransformData CalculateTransformData(DoorAuthoring authoring, DoorConfig config)
            {
                var data = new DoorTransformData();
                var doorType = ComputeDoorType(config.doorCount, config.doorMovement);

                if (doorType == DoorType.SlidingSingle || doorType == DoorType.SlidingDouble)
                {
                    // Slide offsets are authored in the door root's LOCAL space and the animation
                    // system applies them to the panels' LocalTransform, so they are stored as-is.
                    // Converting through world here made a shared DoorConfig useless the moment two
                    // doors had different rotations: every door slid along the same world axis, so a
                    // door rotated 90 degrees slid across its doorway instead of along it.
                    var openOffset = config.slideOpenOffset;
                    data.SlideOffset = new float3(openOffset.x, openOffset.y, openOffset.z);

                    data.SlidingStyle = (SlidingStyle)config.slidingStyle;
                    var rightOffset = config.rightSlideOpenOffset;
                    data.RightSlideOffset = new float3(rightOffset.x, rightOffset.y, rightOffset.z);
                    data.OpenRightDoorOnly = (byte)(config.openRightDoorOnly ? 1 : 0);

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
                    oneWayDir = oneWayDir.sqrMagnitude > 0.0001f ? oneWayDir.normalized : Vector3.forward;
                    data.OneWayDirection = new float3(oneWayDir.x, oneWayDir.y, oneWayDir.z);

                    data.SlideOffset = float3.zero;
                    data.InitialPosition = float3.zero;
                }

                return data;
            }
        }
    }
}