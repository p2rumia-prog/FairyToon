// Adapted from lilToon 2.4.1 (MIT, Copyright (c) 2020-2024 lilxyzw).
// https://github.com/lilxyzw/lilToon/blob/e698a2f791bd28dd1aabf227c74f14bedeca3587/Assets/lilToon/Editor/lilLanguageManager.cs
// https://github.com/lilxyzw/lilToon/blob/e698a2f791bd28dd1aabf227c74f14bedeca3587/Assets/lilToon/Editor/Localization/Localization.cs
// Lookup and the editor-mode toolbar are FairyToon's. Strings that match lilToon live in FairyToonL10n.Catalog.cs.
// See THIRD_PARTY_NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace NW.FairyToon.Editor
{
    internal enum FairyToonEditorMode
    {
        Advanced = 0,
        Preset = 1,
        ShaderSetting = 2
    }

    internal static partial class FairyToonL10n
    {
        public const string LanguageKey = "NW.FairyToon.GUI.Language";
        public const string ModeKey = "NW.FairyToon.GUI.EditorMode";

        private static readonly string[] LanguageCodes = { "en", "ja", "zh-Hans" };
        private static readonly string[] LanguageNames = { "English", "日本語", "简体中文" };

        private static bool s_Loaded;

        public static string Language { get; private set; } = "en";

        public static FairyToonEditorMode Mode { get; private set; } = FairyToonEditorMode.Advanced;

        public static void EnsureLoaded()
        {
            if (s_Loaded) return;
            s_Loaded = true;

            // NW.FairyToon.GUI.Advanced was Simple (false) or Advanced (true). Neither value is
            // Preset or Shader Setting, so both land on the full Advanced page. The old key is left in place.
            if (!EditorPrefs.HasKey(ModeKey))
                EditorPrefs.SetInt(ModeKey, (int)FairyToonEditorMode.Advanced);
            int stored = EditorPrefs.GetInt(ModeKey, (int)FairyToonEditorMode.Advanced);
            Mode = stored >= 0 && stored <= (int)FairyToonEditorMode.ShaderSetting
                ? (FairyToonEditorMode)stored
                : FairyToonEditorMode.Advanced;

            if (!EditorPrefs.HasKey(LanguageKey))
                EditorPrefs.SetString(LanguageKey, DetectEditorLanguage());
            string code = EditorPrefs.GetString(LanguageKey, "en");
            Language = Array.IndexOf(LanguageCodes, code) >= 0 ? code : "en";
        }

        public static string Text(string key)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(key)) return key;
            Dictionary<string, string> table = Table(Language);
            if (table != null && table.TryGetValue(key, out string value) && value.Length > 0) return value;
            if (Language != "en" && En.TryGetValue(key, out string english) && english.Length > 0) return english;
            return key;
        }

        public static string Property(string name, string displayName)
        {
            string key = "p:" + name;
            if (En.ContainsKey(key) || Ja.ContainsKey(key) || Zh.ContainsKey(key)) return Text(key);
            return Text(displayName);
        }

        public static void DrawLanguagePopup()
        {
            EnsureLoaded();
            int index = Array.IndexOf(LanguageCodes, Language);
            if (index < 0) index = 0;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(Text("Language"), index, LanguageNames);
            if (!EditorGUI.EndChangeCheck() || next == index) return;
            Language = LanguageCodes[next];
            EditorPrefs.SetString(LanguageKey, Language);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        public static void DrawModeToolbar()
        {
            EnsureLoaded();
            string[] labels =
            {
                Text("sEditorModeAdvanced"),
                Text("sEditorModePreset"),
                Text("sEditorModeShaderSetting")
            };
            EditorGUI.BeginChangeCheck();
            int next = GUILayout.Toolbar((int)Mode, labels);
            if (!EditorGUI.EndChangeCheck()) return;
            Mode = (FairyToonEditorMode)next;
            EditorPrefs.SetInt(ModeKey, next);
        }

        private static Dictionary<string, string> Table(string language)
        {
            switch (language)
            {
                case "ja": return Ja;
                case "zh-Hans": return Zh;
                case "en": return En;
                default:
                    throw new InvalidOperationException("Unhandled FairyToon language " + language);
            }
        }

        private static string DetectEditorLanguage()
        {
            SystemLanguage editor = ReadUnityEditorLanguage();
            switch (editor)
            {
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.Chinese: return "zh-Hans";
                case SystemLanguage.English: return "en";
                case SystemLanguage.Unknown: break;
                default: break;
            }
            string name = CultureInfo.CurrentUICulture.Name;
            if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
            if (name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                || name.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)
                || name.Equals("zh-SG", StringComparison.OrdinalIgnoreCase))
                return "zh-Hans";
            return "en";
        }

        private static SystemLanguage ReadUnityEditorLanguage()
        {
            try
            {
                Type database = Type.GetType("UnityEditor.LocalizationDatabase, UnityEditor.CoreModule")
                    ?? Type.GetType("UnityEditor.LocalizationDatabase, UnityEditor");
                if (database == null) return SystemLanguage.Unknown;
                PropertyInfo property = database.GetProperty("currentEditorLanguage",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.GetValue(null) is SystemLanguage language) return language;
                MethodInfo method = database.GetMethod("GetCurrentEditorLanguage",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                {
                    object value = method.Invoke(null, null);
                    if (value is SystemLanguage named) return named;
                    if (value is int index && Enum.IsDefined(typeof(SystemLanguage), index))
                        return (SystemLanguage)index;
                }
            }
            catch (Exception)
            {
                // Reflection is a best effort. CurrentUICulture is the fallback.
            }
            return SystemLanguage.Unknown;
        }
    }
}
