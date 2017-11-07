using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace AresEditor.ArcReactor {

    static class MatrixViewer {

        enum MatrixFormula {
            Overall,
            Identity,
            Scale,
            Translate,
            RotateX,
            RotateY,
            RotateZ,
            ScaleX,
            ScaleY,
            ScaleZ,
            TranslateX,
            TranslateY,
            TranslateZ,
        }

        static bool _visible = true;
        static bool _raw_memory_layout = false;
        static bool _usage_tips = false;
        static bool _useWorldSpace = false;
        static MatrixFormula _formula = MatrixFormula.Overall;

        struct Prop {
            public bool editable;
            public Color color;
            public String usage;
            public float value;
        }

        static Prop[] _props = new Prop[ 16 ] {
            new Prop(){ editable = false, color = Color.red + Color.yellow, usage = "xs,cy,cz", value = 1 },
            new Prop(){ editable = false, color = Color.red * 2, usage = "sz", value = 0 },
            new Prop(){ editable = false, color = Color.red * 2, usage = "-sy", value = 1 },
            new Prop(){ editable = false, color = Color.white, usage = "0", value = 0 },

            new Prop(){ editable = false, color = Color.red * 2, usage = "-sz", value = 0 },
            new Prop(){ editable = false, color = Color.red + Color.yellow, usage = "ys,cx,cz", value = 1 },
            new Prop(){ editable = false, color = Color.red * 2, usage = "sx", value = 0 },
            new Prop(){ editable = false, color = Color.white, usage = "0", value = 0 },

            new Prop(){ editable = false, color = Color.red * 2, usage = "sy", value = 0 },
            new Prop(){ editable = false, color = Color.red * 2, usage = "-sx", value = 0 },
            new Prop(){ editable = false, color = Color.red + Color.yellow, usage = "zs,cx,cy", value = 1 },
            new Prop(){ editable = false, color = Color.white, usage = "0", value = 0 },

            new Prop(){ editable = true, color = Color.green, usage = "tx", value = 0 },
            new Prop(){ editable = true, color = Color.green, usage = "ty", value = 0 },
            new Prop(){ editable = true, color = Color.green, usage = "tz", value = 0 },
            new Prop(){ editable = false, color = Color.white, usage = "1", value = 1 },
        };

        public static Action<Transform> OnInspectorGUI = null;

        static MatrixViewer() {
            OnInspectorGUI = _OnInspectorGUI;
        }

        static Quaternion ExtractRotation2( ref Matrix4x4 m ) {
            Vector3 s = ExtractScale( ref m );
            float m00 = m[ 0, 0 ] / s.x;
            float m01 = m[ 0, 1 ] / s.y;
            float m02 = m[ 0, 2 ] / s.z;
            float m10 = m[ 1, 0 ] / s.x;
            float m11 = m[ 1, 1 ] / s.y;
            float m12 = m[ 1, 2 ] / s.z;
            float m20 = m[ 2, 0 ] / s.x;
            float m21 = m[ 2, 1 ] / s.y;
            float m22 = m[ 2, 2 ] / s.z;
            Quaternion q = new Quaternion();
            q.w = Mathf.Sqrt( Mathf.Max( 0, 1 + m00 + m11 + m22 ) ) / 2;
            q.x = Mathf.Sqrt( Mathf.Max( 0, 1 + m00 - m11 - m22 ) ) / 2;
            q.y = Mathf.Sqrt( Mathf.Max( 0, 1 - m00 + m11 - m22 ) ) / 2;
            q.z = Mathf.Sqrt( Mathf.Max( 0, 1 - m00 - m11 + m22 ) ) / 2;
            q.x *= Mathf.Sign( q.x * ( m21 - m12 ) );
            q.y *= Mathf.Sign( q.y * ( m02 - m20 ) );
            q.z *= Mathf.Sign( q.z * ( m10 - m01 ) );
            float qMagnitude = Mathf.Sqrt( q.w * q.w + q.x * q.x + q.y * q.y + q.z * q.z );
            q.w /= qMagnitude;
            q.x /= qMagnitude;
            q.y /= qMagnitude;
            q.z /= qMagnitude;
            return q;
        }

        static Vector3 QuaternionToEuler( Quaternion q ) {
            Vector3 result;
            float test = q.x * q.y + q.z * q.w;
            // singularity at north pole
            if ( test > 0.499f ) {
                result.x = 0;
                result.y = 2 * Mathf.Atan2( q.x, q.w );
                result.z = Mathf.PI / 2;
            } else if ( test < -0.499f ) {
                // singularity at south pole
                result.x = 0;
                result.y = -2 * Mathf.Atan2( q.x, q.w );
                result.z = -Mathf.PI / 2;
            } else {
                result.x = Mathf.Rad2Deg * Mathf.Atan2( 2 * q.x * q.w - 2 * q.y * q.z, 1 - 2 * q.x * q.x - 2 * q.z * q.z );
                result.y = Mathf.Rad2Deg * Mathf.Atan2( 2 * q.y * q.w - 2 * q.x * q.z, 1 - 2 * q.y * q.y - 2 * q.z * q.z );
                result.z = Mathf.Rad2Deg * Mathf.Asin( 2 * q.x * q.y + 2 * q.z * q.w );
                if ( result.x < 0 ) {
                    result.x += 360;
                }
                if ( result.y < 0 ) {
                    result.y += 360;
                }
                if ( result.z < 0 ) {
                    result.z += 360;
                }
            }
            return result;
        }

        static Quaternion ExtractRotation( ref Matrix4x4 matrix ) {
            Vector3 forward;
            forward.x = matrix.m02;
            forward.y = matrix.m12;
            forward.z = matrix.m22;
            Vector3 upwards;
            upwards.x = matrix.m01;
            upwards.y = matrix.m11;
            upwards.z = matrix.m21;
            return Quaternion.LookRotation( forward, upwards );
        }

        static Vector3 ExtractPosition( ref Matrix4x4 matrix ) {
            Vector3 position;
            position.x = matrix.m03;
            position.y = matrix.m13;
            position.z = matrix.m23;
            return position;
        }

        static Vector3 ExtractScale( ref Matrix4x4 matrix ) {
            Vector3 scale;
            scale.x = new Vector4( matrix.m00, matrix.m10, matrix.m20, matrix.m30 ).magnitude;
            scale.y = new Vector4( matrix.m01, matrix.m11, matrix.m21, matrix.m31 ).magnitude;
            scale.z = new Vector4( matrix.m02, matrix.m12, matrix.m22, matrix.m32 ).magnitude;
            return scale;
        }

        static void _DrawMatrixColumnMajor( ref Matrix4x4 mat, Func<int, float, KeyValuePair<bool, float>> func ) {
            EditorGUILayout.BeginHorizontal();
            var index = 0;
            for ( int c = 0; c < 4; ++c ) {
                if ( c > 0 ) {
                    GUILayout.Box( "", GUILayout.Height( 4 * ( EditorGUIUtility.singleLineHeight ) + EditorGUIUtility.standardVerticalSpacing ), GUILayout.Width( 1 ) );
                }
                EditorGUILayout.BeginVertical();
                for ( int r = 0; r < 4; ++r ) {
                    EditorGUILayout.BeginHorizontal();
                    var _enabled = GUI.enabled;
                    GUI.enabled = false;
                    EditorGUILayout.LabelField( String.Format( "m{0}{1}", c, r ), GUILayout.Width( 28 ) );
                    GUI.enabled = _props[ index ].editable;
                    var _color = GUI.color;
                    GUI.color = _props[ index ].color;
                    var ret = func( index, mat[ r, c ] );
                    if ( ret.Key ) {
                        mat[ r, c ] = ret.Value;
                    }
                    GUI.color = _color;
                    GUI.enabled = _enabled;
                    EditorGUILayout.EndHorizontal();
                    ++index;
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
        }

        static unsafe void _DrawMatrixRowMajor( Matrix4x4* mat, Action<int, IntPtr> func ) {
            var index = 0;
            float* p = ( float* )mat;
            for ( int r = 0; r < 4; ++r ) {
                if ( r > 0 ) {
                    GUILayout.Box( "", GUILayout.ExpandWidth( true ), GUILayout.Height( 1 ) );
                }
                EditorGUILayout.BeginHorizontal();
                for ( int c = 0; c < 4; ++c ) {
                    var _enabled = GUI.enabled;
                    GUI.enabled = false;
                    EditorGUILayout.LabelField( String.Format( "{0:00}", r * 4 + c ), GUILayout.Width( 28 ) );
                    GUI.enabled = _props[ index ].editable;
                    var _color = GUI.color;
                    GUI.color = _props[ index ].color;
                    func( index, ( IntPtr )p );
                    GUI.color = _color;
                    GUI.enabled = _enabled;
                    ++p;
                    ++index;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        static void DrawFomula( int _index ) {
            var usage = _props[ _index ].usage;
            var f = _formula.ToString();
            Func<Char, int> charToAxisMask = c => {
                c = Char.ToUpper( c );
                int _axisMask = 0xff;
                if ( c == 'X' ) {
                    _axisMask = 1 << 0;
                } else if ( c == 'Y' ) {
                    _axisMask = 1 << 1;
                } else if ( c == 'Z' ) {
                    _axisMask = 1 << 2;
                }
                return _axisMask;
            };
            var axisMask = charToAxisMask( f[ f.Length - 1 ] );
            switch ( _formula ) {
            case MatrixFormula.Overall:
                EditorGUILayout.TextField( usage );
                break;
            case MatrixFormula.Identity:
                EditorGUILayout.FloatField( _props[ _index ].value );
                break;
            case MatrixFormula.RotateX:
            case MatrixFormula.RotateY:
            case MatrixFormula.RotateZ: {
                    var _usages = usage.Split( ',' );
                    var found = false;
                    for ( int i = 0; i < _usages.Length; ++i ) {
                        var _usage = _usages[ i ];
                        // sin & cos tag
                        var sign = _usage.StartsWith( "-" ) ? "-" : "";
                        _usage = _usage.TrimStart( '-' );
                        var sin = _usage.StartsWith( "s" );
                        var cos = _usage.StartsWith( "c" );
                        var axis = charToAxisMask( _usage[ _usage.Length - 1 ] );
                        if ( ( sin || cos ) && ( axis & axisMask ) != 0 ) {
                            var usage_ = String.Format( "{0}{1}( {2} )", sign, sin ? "sin" : "cos", _usage[ _usage.Length - 1 ] );
                            EditorGUILayout.TextField( usage_ );
                            found = true;
                            break;
                        }
                    }
                    if ( !found ) {
                        EditorGUILayout.FloatField( _props[ _index ].value );
                    }
                }
                break;
            case MatrixFormula.Scale:
            case MatrixFormula.ScaleX:
            case MatrixFormula.ScaleY:
            case MatrixFormula.ScaleZ: {
                    var _usages = usage.Split( ',' );
                    var found = false;
                    for ( int i = 0; i < _usages.Length; ++i ) {
                        var _usage = _usages[ i ];
                        if ( _usage.EndsWith( "s" ) ) {
                            var tag = Char.ToUpper( _usage[ 0 ] );
                            var mask = charToAxisMask( tag );
                            if ( ( axisMask & mask ) != 0 ) {
                                var usage_ = String.Format( "scale{0}", tag );
                                EditorGUILayout.TextField( usage_ );
                                found = true;
                                break;
                            }
                        }
                    }
                    if ( !found ) {
                        EditorGUILayout.FloatField( _props[ _index ].value );
                    }
                }
                break;
            case MatrixFormula.Translate:
            case MatrixFormula.TranslateX:
            case MatrixFormula.TranslateY:
            case MatrixFormula.TranslateZ: {
                    var _usage = String.Empty;
                    if ( usage.StartsWith( "t" ) ) {
                        var tag = Char.ToUpper( usage[ usage.Length - 1 ] );
                        var mask = charToAxisMask( tag );
                        if ( ( axisMask & mask ) != 0 ) {
                            _usage = String.Format( "trans{0}", tag );
                        }
                    }
                    if ( !String.IsNullOrEmpty( _usage ) ) {
                        EditorGUILayout.TextField( _usage );
                    } else {
                        EditorGUILayout.FloatField( _props[ _index ].value );
                    }
                }
                break;
            }
        }

        static unsafe Matrix4x4 DrawMatrix( Matrix4x4 mat ) {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Separator();
            _useWorldSpace = EditorGUILayout.ToggleLeft( "World Space", _useWorldSpace );
            _raw_memory_layout = EditorGUILayout.ToggleLeft( "Memory Layout Mode", _raw_memory_layout );
            EditorGUILayout.Separator();

            var labelWidth = EditorGUIUtility.labelWidth;
            try {
                EditorGUIUtility.labelWidth = 24;
                if ( !_raw_memory_layout ) {
                    _DrawMatrixColumnMajor(
                        ref mat,
                        ( _index, v ) => new KeyValuePair<bool, float>( true, EditorGUILayout.FloatField( v ) )
                    );
                    EditorGUILayout.Separator();
                    _usage_tips = EditorGUILayout.ToggleLeft( "Usage Tips", _usage_tips );
                    if ( _usage_tips ) {
                        EditorGUILayout.Separator();
                        _formula = ( MatrixFormula )EditorGUILayout.EnumPopup( _formula );
                        EditorGUILayout.Separator();
                        _DrawMatrixColumnMajor(
                            ref mat,
                            ( _index, v ) => {
                                DrawFomula( _index );
                                return new KeyValuePair<bool, float>( false, 0.0f );
                            }
                        );
                    }
                } else {
                    float* p = ( float* )&mat;
                    _DrawMatrixRowMajor(
                        &mat,
                        ( _index, ptr ) => {
                            float* _p = ( float* )ptr;
                            *_p = EditorGUILayout.FloatField( *_p );
                        }
                    );
                    EditorGUILayout.Separator();
                    _usage_tips = EditorGUILayout.ToggleLeft( "Usage Tips", _usage_tips );
                    if ( _usage_tips ) {
                        EditorGUILayout.Separator();
                        _formula = ( MatrixFormula )EditorGUILayout.EnumPopup( _formula );
                        EditorGUILayout.Separator();
                        _DrawMatrixRowMajor( &mat, ( _index, ptr ) => DrawFomula( _index ) );
                    }
                }
            } finally {
                EditorGUIUtility.labelWidth = labelWidth;
                GUI.enabled = true;
            }
            EditorGUILayout.Separator();
            EditorGUILayout.EndVertical();
            return mat;
        }

        static void _OnInspectorGUI( Transform target ) {
            Transform transform = target;
            if ( transform != null ) {
                if ( _useWorldSpace ) {
                    var mat = transform.localToWorldMatrix;
                    var newMat = DrawMatrix( mat );
                    if ( newMat != mat ) {
                        Undo.RecordObject( transform, "Assign From Matrix Viewer" );
                        transform.rotation = ExtractRotation( ref newMat );
                        transform.position = ExtractPosition( ref newMat );
                    }
                } else {
                    var mat = Matrix4x4.TRS( transform.localPosition, transform.localRotation, transform.localScale );
                    var newMat = DrawMatrix( mat );
                    if ( newMat != mat ) {
                        Undo.RecordObject( transform, "Assign From Matrix Viewer" );
                        transform.localScale = ExtractScale( ref newMat );
                        transform.localRotation = ExtractRotation( ref newMat );
                        transform.localPosition = ExtractPosition( ref newMat );
                    }
                }
            }
        }
    }

    class MatrixViewerWindow : EditorWindow {

        static MatrixViewerWindow m_window = null;

        [MenuItem( "Tools/MatrixViewer" )]
        static void Init() {
            if ( m_window == null ) {
                m_window = EditorWindow.GetWindow<MatrixViewerWindow>( "Matrix Viewer", true, typeof( EditorWindow ) );
            }
            m_window.minSize = new Vector2( 400, 320 );
            m_window.Show();
        }

        void OnEnable() {
            EditorApplication.update += _Repaint;
        }

        void OnDisable() {
            EditorApplication.update -= _Repaint;
        }

        void _Repaint() {
            Repaint();
        }

        void OnGUI() {
            var go = Selection.activeGameObject;
            if ( go != null ) {
                MatrixViewer.OnInspectorGUI( go.transform );
            }
        }

        void OnDestroy() {
            m_window = null;
        }
    }
}
