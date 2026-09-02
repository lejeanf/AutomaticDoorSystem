using System;
using UnityEngine;

namespace AutomaticDoorSystem
{
    /// <summary>
    /// Shows a serialized field only while every listed sibling enum/int/bool property equals its
    /// paired value; otherwise the field takes no inspector space at all. Set <see cref="Header"/>
    /// to draw a bold section title above the field while it is visible, so a whole section
    /// (title included) disappears with its fields - a plain [Header] decorator would keep drawing
    /// on its own.
    ///
    /// Drawn by AutomaticDoorSystem.Editor.ShowWhenDrawer, which works inside Unity's default
    /// inspector AND inside the propertyDrawer package's inline ScriptableObject foldout (both go
    /// through EditorGUI.PropertyField), so a DoorConfig looks the same edited from its asset or
    /// from a DoorAuthoring. The drawer also honours [Range] on the same field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class ShowWhenAttribute : PropertyAttribute
    {
        public readonly string[] PropertyNames;
        public readonly int[] Values;

        /// <summary>
        /// Unity runs ONE PropertyDrawer per field, picked by attribute order. This one must win
        /// over [Range] so it can hide the field; it then draws the slider itself.
        /// </summary>
        private const int DrawFirst = -1000;

        /// <summary>Optional bold section title drawn above the field while it is visible.</summary>
        public string Header { get; set; }

        /// <summary>Visible while <paramref name="property"/> (an enum, int or bool) equals <paramref name="value"/>.</summary>
        public ShowWhenAttribute(string property, int value)
        {
            PropertyNames = new[] { property };
            Values = new[] { value };
            order = DrawFirst;
        }

        /// <summary>Visible while BOTH conditions hold.</summary>
        public ShowWhenAttribute(string property, int value, string property2, int value2)
        {
            PropertyNames = new[] { property, property2 };
            Values = new[] { value, value2 };
            order = DrawFirst;
        }

        /// <summary>Visible while all THREE conditions hold.</summary>
        public ShowWhenAttribute(string property, int value, string property2, int value2, string property3, int value3)
        {
            PropertyNames = new[] { property, property2, property3 };
            Values = new[] { value, value2, value3 };
            order = DrawFirst;
        }
    }
}
