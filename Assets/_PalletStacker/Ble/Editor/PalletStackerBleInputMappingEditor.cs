using UnityEditor;
using UnityEngine;

namespace ElectricPalletStackers.Ble.Editor
{
    [CustomEditor(typeof(PalletStackerBleInputMapping))]
    public sealed class PalletStackerBleInputMappingEditor : UnityEditor.Editor
    {
        private static readonly GUIContent[] BitLabels =
        {
            new GUIContent("Bit 0  (0x01)"),
            new GUIContent("Bit 1  (0x02)"),
            new GUIContent("Bit 2  (0x04)"),
            new GUIContent("Bit 3  (0x08)"),
            new GUIContent("Bit 4  (0x10)"),
            new GUIContent("Bit 5  (0x20)"),
            new GUIContent("Bit 6  (0x40)"),
            new GUIContent("Bit 7  (0x80)")
        };

        private static readonly int[] BitValues =
        {
            1 << 0,
            1 << 1,
            1 << 2,
            1 << 3,
            1 << 4,
            1 << 5,
            1 << 6,
            1 << 7
        };

        private static readonly string[] FlagPropertyNames =
        {
            "_enabledFlagMask",
            "_stopFlagMask",
            "_emergencyStopFlagMask",
            "_hornFlagMask",
            "_slowModeFlagMask"
        };

        private SerializedProperty _enabledFlagMask;
        private SerializedProperty _stopFlagMask;
        private SerializedProperty _emergencyStopFlagMask;
        private SerializedProperty _hornFlagMask;
        private SerializedProperty _slowModeFlagMask;

        private void OnEnable()
        {
            _enabledFlagMask = serializedObject.FindProperty("_enabledFlagMask");
            _stopFlagMask = serializedObject.FindProperty("_stopFlagMask");
            _emergencyStopFlagMask = serializedObject.FindProperty("_emergencyStopFlagMask");
            _hornFlagMask = serializedObject.FindProperty("_hornFlagMask");
            _slowModeFlagMask = serializedObject.FindProperty("_slowModeFlagMask");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    "Script",
                    MonoScript.FromScriptableObject((PalletStackerBleInputMapping)target),
                    typeof(MonoScript),
                    false);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Flags", EditorStyles.boldLabel);
            DrawBitPopup(_enabledFlagMask, "Enabled");
            DrawBitPopup(_stopFlagMask, "Stop");
            DrawBitPopup(_emergencyStopFlagMask, "Emergency Stop");
            DrawBitPopup(_hornFlagMask, "Horn");
            DrawBitPopup(_slowModeFlagMask, "Slow Mode");

            if (HasDuplicateFlags())
            {
                EditorGUILayout.HelpBox(
                    "Each CONTROL_STATE flag must use a different bit.",
                    MessageType.Error);
            }

            EditorGUILayout.Space();
            DrawPropertiesExcluding(serializedObject, BuildExcludedProperties());
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawBitPopup(SerializedProperty property, string label)
        {
            property.intValue = EditorGUILayout.IntPopup(
                new GUIContent(label, property.tooltip),
                property.intValue,
                BitLabels,
                BitValues);
        }

        private bool HasDuplicateFlags()
        {
            int combined = 0;
            SerializedProperty[] properties =
            {
                _enabledFlagMask,
                _stopFlagMask,
                _emergencyStopFlagMask,
                _hornFlagMask,
                _slowModeFlagMask
            };

            foreach (SerializedProperty property in properties)
            {
                int mask = property.intValue;
                if ((combined & mask) != 0) return true;
                combined |= mask;
            }

            return false;
        }

        private static string[] BuildExcludedProperties()
        {
            string[] excluded = new string[FlagPropertyNames.Length + 1];
            excluded[0] = "m_Script";
            for (int index = 0; index < FlagPropertyNames.Length; index++)
                excluded[index + 1] = FlagPropertyNames[index];
            return excluded;
        }
    }
}
