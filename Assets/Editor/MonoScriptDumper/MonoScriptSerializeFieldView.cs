using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;

public class MonoScriptSerializeFieldView : EditorWindow {

    static UnityEngine.Object _monoScript = null;
    static String _dumpedResult = String.Empty;
    static Vector2 _viewPos = Vector2.zero;

    [MenuItem( "Tools/SerializeFieldView" )]
    static void Init() {
        var window = ( MonoScriptSerializeFieldView )EditorWindow.GetWindow( typeof( MonoScriptSerializeFieldView ), true, "MonoScript SerializeField View" );
        window.Show();
    }

    void OnDestroy() {
        _monoScript = null;
    }

    static Type FindType( String name ) {
        var a = AppDomain.CurrentDomain.GetAssemblies();
        for ( int i = 0; i < a.Length; ++i ) {
            var types = a[ i ].GetTypes();
            for ( int j = 0; j < types.Length; ++j ) {
                var t = types[ j ];
                if ( t.Name == name ) {
                    if ( typeof( UnityEngine.MonoBehaviour ).IsAssignableFrom( t ) ) {
                        return t;
                    }
                }
            }
        }
        return null;
    }

    void OnGUI() {
        EditorGUILayout.BeginVertical();
        EditorGUILayout.Space();
        if ( _monoScript == null ) {
            EditorGUILayout.HelpBox( "Drag a MonoBehaviour script to view all of its serialize fields.", MessageType.Info );
        }
        EditorGUILayout.BeginHorizontal();
        var oldValue = _monoScript;
        EditorGUIUtility.labelWidth = 100;
        _monoScript = EditorGUILayout.ObjectField( "MonoBehaviour:", _monoScript, typeof( UnityEngine.Object ), true ) as UnityEngine.Object;
        if ( _monoScript != oldValue ) {
            _dumpedResult = String.Empty;
            _viewPos = Vector2.zero;
            if ( _monoScript != null ) {
                _dumpedResult = String.Empty;
                Type type = null;
                type = FindType( _monoScript.name );
                if ( type != null ) {
                    var info = MonoScriptDumper.Dump( type );
                    _dumpedResult = info as String;
                    _viewPos = Vector2.zero;
                } else {
                    Debug.LogError( String.Format( "Type: {0} not found.", _monoScript.name ) );
                }
            }
        }
        EditorGUILayout.EndHorizontal();
        if ( !String.IsNullOrEmpty( _dumpedResult ) ) {
            EditorGUILayout.Space();
            _viewPos = EditorGUILayout.BeginScrollView( _viewPos );
            EditorGUILayout.TextArea( _dumpedResult );
            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.Space();
        EditorGUILayout.EndVertical();
    }
}

