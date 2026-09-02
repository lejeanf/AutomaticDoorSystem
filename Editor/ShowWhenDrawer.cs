#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AutomaticDoorSystem.Editor
{
    /// <summary>
    /// Draws a [ShowWhen] field only while its conditions hold. Hidden fields report a NEGATIVE
    /// height (minus the standard spacing) so they leave no gap - both Unity's default inspector
    /// and the propertyDrawer package's inline ScriptableObject foldout add that spacing after
    /// every property. Visibility is evaluated in GetPropertyHeight as well as OnGUI, never cached,
    /// so the layout is right on the first frame (the DrawIf drawer in propertyDrawer caches it
    /// from OnGUI and lays out one frame late).
    /// </summary>
    [CustomPropertyDrawer(typeof(ShowWhenAttribute))]
    public class ShowWhenDrawer : PropertyDrawer
    {
        private static readonly float HeaderHeight = EditorGUIUtility.singleLineHeight * 1.5f;

        private ShowWhenAttribute ShowWhen => (ShowWhenAttribute)attribute;

        private RangeAttribute Range => fieldInfo?.GetCustomAttribute<RangeAttribute>();

        private bool IsVisible(SerializedProperty property)
        {
            var conditions = ShowWhen;
            for (var i = 0; i < conditions.PropertyNames.Length; i++)
            {
                var sibling = FindSibling(property, conditions.PropertyNames[i]);
                if (sibling == null)
                {
                    Debug.LogWarning($"[ShowWhen] '{property.propertyPath}': no sibling property named '{conditions.PropertyNames[i]}'.");
                    return true;
                }

                int value;
                switch (sibling.propertyType)
                {
                    case SerializedPropertyType.Enum: value = sibling.enumValueIndex; break;
                    case SerializedPropertyType.Integer: value = sibling.intValue; break;
                    case SerializedPropertyType.Boolean: value = sibling.boolValue ? 1 : 0; break;
                    default:
                        Debug.LogWarning($"[ShowWhen] '{conditions.PropertyNames[i]}' is not an enum, int or bool.");
                        return true;
                }

                if (value != conditions.Values[i]) return false;
            }
            return true;
        }

        private static SerializedProperty FindSibling(SerializedProperty property, string name)
        {
            var path = property.propertyPath;
            var dot = path.LastIndexOf('.');
            var siblingPath = dot < 0 ? name : path.Substring(0, dot + 1) + name;
            return property.serializedObject.FindProperty(siblingPath);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!IsVisible(property))
                return -EditorGUIUtility.standardVerticalSpacing;

            var height = Range != null
                ? EditorGUIUtility.singleLineHeight
                : EditorGUI.GetPropertyHeight(property, label, true);

            if (!string.IsNullOrEmpty(ShowWhen.Header))
                height += HeaderHeight;

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!IsVisible(property)) return;

            var header = ShowWhen.Header;
            if (!string.IsNullOrEmpty(header))
            {
                // Same look as Unity's [Header]: bold, sitting on the lower part of its slot.
                var headerRect = new Rect(position.x, position.y + HeaderHeight - EditorGUIUtility.singleLineHeight,
                    position.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(headerRect, header, EditorStyles.boldLabel);
                position.y += HeaderHeight;
                position.height -= HeaderHeight;
            }

            EditorGUI.BeginProperty(position, label, property);

            // Only one PropertyDrawer runs per field, so the [Range] slider is reproduced here
            // instead of relying on Unity chaining to RangeDrawer.
            var range = Range;
            if (range != null && property.propertyType == SerializedPropertyType.Float)
            {
                EditorGUI.Slider(position, property, range.min, range.max, label);
            }
            else if (range != null && property.propertyType == SerializedPropertyType.Integer)
            {
                EditorGUI.IntSlider(position, property, (int)range.min, (int)range.max, label);
            }
            else
            {
                EditorGUI.PropertyField(position, property, label, true);
            }

            EditorGUI.EndProperty();
        }
    }
}
#endif
