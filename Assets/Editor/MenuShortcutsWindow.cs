using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Reflection;

public class MenuShortcutsWindow : EditorWindow {

    enum Page {
        Shortcuts,
        All
    }

    class MenuItemData {
        static Char[] separator = new Char[] { '/', ' ' };
        public String menuItemPath = String.Empty;
        public String name = String.Empty;
        public String[] keywords = null;
        public String assemblyName = String.Empty;
        public void Init() {
            name = Path.GetFileNameWithoutExtension( menuItemPath );
            keywords = menuItemPath.Split( separator, StringSplitOptions.RemoveEmptyEntries );
        }
    }

    static MenuShortcutsWindow s_window = null;
    static SceneView.OnSceneFunc s_OnSceneGUI = null;
    static EditorApplication.CallbackFunction s_OnEditorWindowUpdate = null;

    static SortedDictionary<String, MenuItemData> s_allMenuItems = new SortedDictionary<String, MenuItemData>();
    static SortedDictionary<String, MenuItemData> s_shortcuts = new SortedDictionary<String, MenuItemData>();
    static Vector2 s_scrollViewPos = Vector2.zero;
    static Vector2 s_shortcutsViewPos = Vector2.zero;
    static String s_search = String.Empty;
    static List<String> s_assemblies = new List<String>();
    static Page s_page = Page.Shortcuts;

    [MenuItem( "Window/ShortcutWindow", false, 100 )]
    static void Init() {
        if ( s_window == null ) {
            s_window = ScriptableObject.CreateInstance<MenuShortcutsWindow>();
            s_window.titleContent = new GUIContent( "Shortcuts" );
            CheckInitAllMenuItems();
        }
        if ( s_shortcuts.Count == 0 ) {
            s_page = Page.All;
        } else {
            s_page = Page.Shortcuts;
        }
        s_window.Show();
        s_OnSceneGUI = OnSceneGUI;
        s_OnEditorWindowUpdate = OnEditorWindowUpdate;
        EditorApplication.update += s_OnEditorWindowUpdate;
        SceneView.onSceneGUIDelegate += s_OnSceneGUI;
    }

    static void CheckInitAllMenuItems() {
        if ( s_allMenuItems == null || s_allMenuItems.Count == 0 ) {
            var items = SearchAllMenuItems();
            s_allMenuItems = s_allMenuItems ?? new SortedDictionary<String, MenuItemData>();
            s_allMenuItems.Clear();
            var assemblies = new HashSet<String>();
            for ( int i = 0; i < items.Count; ++i ) {
                var item = new MenuItemData();
                var assemblyName = items[ i ].Value;
                assemblies.Add( assemblyName );
                s_allMenuItems.Add( items[ i ].Key, item );
                item.menuItemPath = items[ i ].Key;
                item.Init();
            }
            s_assemblies = assemblies.ToList();
            s_assemblies.Sort();
            Load();
        }
    }

    static void Load() {
        s_search = EditorPrefs.GetString( "MenuShortcutsWindow-search", String.Empty );
        s_shortcuts.Clear();
        var shortcuts = EditorPrefs.GetString( "MenuShortcutsWindow-shortcuts", String.Empty ).Split( ';' );
        if ( shortcuts.Length > 0 ) {
            for ( int i = 0; i < shortcuts.Length; ++i ) {
                MenuItemData item;
                if ( s_allMenuItems.TryGetValue( shortcuts[ i ], out item ) && item != null ) {
                    s_shortcuts.Add( shortcuts[ i ], item );
                }
            }
        }
        if ( s_shortcuts.Count == 0 ) {
            s_page = Page.All;
        } else {
            s_page = Page.Shortcuts;
        }
    }

    static void Save() {
        if ( s_shortcuts != null && s_shortcuts.Count > 0 ) {
            var values = s_shortcuts.Keys.ToArray();
            Array.Sort( values );
            EditorPrefs.SetString( "MenuShortcutsWindow-shortcuts", String.Join( ";", values ) );
        } else {
            EditorPrefs.DeleteKey( "MenuShortcutsWindow-shortcuts" );
        }
        if ( !String.IsNullOrEmpty( s_search ) ) {
            EditorPrefs.SetString( "MenuShortcutsWindow-search", s_search );
        } else {
            EditorPrefs.DeleteKey( "MenuShortcutsWindow-search" );
        }
    }

    static List<KeyValuePair<String, String>> SearchAllMenuItems() {
        var ret = new List<KeyValuePair<String, String>>();
        var set = new HashSet<String>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for ( int i = 0; i < assemblies.Length; ++i ) {
            var types = assemblies[ i ].GetTypes();
            var assemblyName = assemblies[ i ].GetName().Name;
            for ( int j = 0; j < types.Length; ++j ) {
                var methods = types[ j ].GetMethods( BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic );
                for ( int n = 0; n < methods.Length; ++n ) {
                    var attrs = methods[ n ].GetCustomAttributes( typeof( MenuItem ), false );
                    if ( attrs.Length > 0 ) {
                        for ( int m = 0; m < attrs.Length; ++m ) {
                            var attr = attrs[ m ] as MenuItem;
                            if ( !String.IsNullOrEmpty( attr.menuItem ) ) {
                                if ( set.Add( attr.menuItem ) ) {
                                    ret.Add( new KeyValuePair<String, String>( attr.menuItem, assemblyName ) );
                                }
                            }
                        }
                    }
                }
            }
        }
        return ret;
    }

    void OnDestroy() {
        Save();
        s_window = null;
        SceneView.onSceneGUIDelegate -= s_OnSceneGUI;
        EditorApplication.update -= s_OnEditorWindowUpdate;
        s_OnSceneGUI = null;
        s_OnEditorWindowUpdate = null;
    }

    void OnSelectionChange() {
        Repaint();
    }

    static void OnEditorWindowUpdate() {
    }

    static void OnSceneGUI( SceneView sceneview ) {
    }

    static void Clear() {
        s_search = String.Empty;
        s_shortcuts.Clear();
        Save();
    }

    static void OnGUI_Shortcuts() {
        s_shortcutsViewPos = EditorGUILayout.BeginScrollView( s_shortcutsViewPos );
        List<String> removeKeys = null;
        foreach ( var kv in s_shortcuts ) {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField( kv.Value.name );
            GUI.color = Color.red;
            if ( GUILayout.Button( "-", GUILayout.Width( 20 ) ) ) {
                removeKeys = removeKeys ?? new List<String>();
                removeKeys.Add( kv.Key );
            }
            GUI.color = Color.green;
            if ( GUILayout.Button( "Excute", GUILayout.Width( 60 ) ) ) {
                EditorApplication.ExecuteMenuItem( kv.Key );
            }
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();
        }
        if ( removeKeys != null ) {
            for ( int i = 0; i < removeKeys.Count; ++i ) {
                s_shortcuts.Remove( removeKeys[ i ] );
            }
            Save();
        }
        EditorGUILayout.EndScrollView();
        if ( GUILayout.Button( "Clear" ) ) {
            Clear();
        }
    }

    static void OnGUI_ShowAll() {
        s_scrollViewPos = EditorGUILayout.BeginScrollView( s_scrollViewPos );
        var changed = false;
        foreach ( var kv in s_allMenuItems ) {
            if ( !String.IsNullOrEmpty( s_search ) && !kv.Key.StartsWith( s_search ) ) {
                continue;
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField( kv.Key );
            if ( s_shortcuts.ContainsKey( kv.Key ) ) {
                GUI.color = Color.red;
                if ( GUILayout.Button( "-", GUILayout.Width( 20 ) ) ) {
                    s_shortcuts.Remove( kv.Key );
                    changed = true;
                }
            } else {
                GUI.color = Color.green;
                if ( GUILayout.Button( "+", GUILayout.Width( 20 ) ) ) {
                    s_shortcuts.Add( kv.Key, kv.Value );
                    changed = true;
                }
            }
            GUI.color = Color.white;
            if ( GUILayout.Button( "Excute", GUILayout.Width( 60 ) ) ) {
                EditorApplication.ExecuteMenuItem( kv.Key );
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        if ( changed ) {
            Save();
        }
    }

    void OnGUI() {
        CheckInitAllMenuItems();
        EditorGUILayout.BeginVertical();
        EditorGUIUtility.labelWidth = 32;
        s_search = EditorGUILayout.TextField( "Find", s_search );
        if ( s_search.StartsWith( " " ) ) {
            var search = s_search.TrimStart( ' ' );
            if ( String.IsNullOrEmpty( search ) ) {
                s_search = String.Empty;
            }
        }

        s_page = ( Page )GUILayout.Toolbar( ( int )s_page, Enum.GetNames( typeof( Page ) ) );
        switch ( s_page ) {
        case Page.Shortcuts:
            OnGUI_Shortcuts();
            break;
        case Page.All:
            OnGUI_ShowAll();
            break;
        }
        EditorGUILayout.EndVertical();
    }
}
