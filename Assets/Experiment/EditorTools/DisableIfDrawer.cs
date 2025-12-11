using UnityEngine;
using UnityEditor;
using System;

namespace InspectorTools {
    [CustomPropertyDrawer(typeof(DisableIfAttribute))]
    public class DisableIfDrawer : PropertyDrawer {

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {

            DisableIfAttribute cond = attribute as DisableIfAttribute;

            SerializedProperty conditionProp = property.serializedObject.FindProperty(cond.conditionField);

            bool shouldDisable = false;

            if (conditionProp != null) {

                switch (conditionProp.propertyType) {
                    case SerializedPropertyType.Enum:
                        shouldDisable = (conditionProp.enumValueIndex == Convert.ToInt32(cond.compareValue));
                        break;

                    case SerializedPropertyType.Boolean:
                        shouldDisable = (conditionProp.boolValue == (bool)cond.compareValue);
                        break;
                }
            }

            bool oldState = GUI.enabled;
            GUI.enabled = !shouldDisable;

            EditorGUI.PropertyField(position, property, label, true);

            GUI.enabled = oldState;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
}
