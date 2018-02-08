using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using UnityEditor;
using UnityEngine;
using System.Threading;
using System.Diagnostics;

namespace AresEditor.ArtistKit {

    public class LightmapViewer : EditorWindow {

        static LightmapViewer m_window = null;
        static Dictionary<String, Vector2[]> _MeshUVsCache = null;
        static Dictionary<Renderer, Vector4> _LightmapUVBoundsCache = null;

        List<KeyValuePair<int, Vector4>> _litmapUVs = null;

        [MenuItem( "Tools/LightmapViewer" )]
        static void Init() {
            if ( m_window == null ) {
                m_window = ( LightmapViewer )EditorWindow.GetWindow( typeof( LightmapViewer ), false, "Lightmap Viewer" );
            }
            m_window.minSize = new Vector2( 400, 300 );
            m_window.position = new Rect( 0, 0, m_window.minSize.x, m_window.minSize.y );
            m_window.Show();
        }

        void OnEnable() {
            Selection.selectionChanged += OnSelectionChanged;
            UpdateCache();
        }

        void OnDisable() {
            _litmapUVs = null;
            Selection.selectionChanged -= OnSelectionChanged;
            if ( _MeshUVsCache != null ) {
                _MeshUVsCache.Clear();
            }
            if ( _LightmapUVBoundsCache != null ) {
                _LightmapUVBoundsCache.Clear();
            }
        }

        void OnDestroy() {
            m_window = null;
        }

        void UpdateCache() {
            _MeshUVsCache = _MeshUVsCache ?? new Dictionary<String, Vector2[]>();
            _LightmapUVBoundsCache = _LightmapUVBoundsCache ?? new Dictionary<Renderer, Vector4>();
            var objs = Selection.gameObjects;
            List<KeyValuePair<int, Vector4>> litmapUVs = null;
            for ( int i = 0; i < objs.Length; ++i ) {
                var rs = objs[ i ].GetComponentsInChildren<Renderer>();
                for ( int j = 0; j < rs.Length; ++j ) {
                    var r = rs[ j ];
                    if ( r != null && r.lightmapIndex >= 0 ) {
                        Mesh m = null;
                        if ( r is MeshRenderer ) {
                            var mf = r.GetComponent<MeshFilter>();
                            if ( mf != null ) {
                                m = mf.sharedMesh;
                            }
                        } else if ( r is SkinnedMeshRenderer ) {
                            var _r = r as SkinnedMeshRenderer;
                            m = _r.sharedMesh;
                        }

                        if ( m != null ) {
                            Vector4 uvBounds;
                            if ( !_LightmapUVBoundsCache.TryGetValue( r, out uvBounds ) ) {
                                var uv = GetMeshUV2( m );
                                if ( uv != null ) {
                                    var __uv = new Vector2[ uv.Length ];
                                    Array.Copy( uv, __uv, uv.Length );
                                    uv = __uv;
                                    litmapUVs = litmapUVs ?? new List<KeyValuePair<int, Vector4>>();
                                    var minx = float.MaxValue;
                                    var miny = float.MaxValue;
                                    var maxx = float.MinValue;
                                    var maxy = float.MinValue;
                                    for ( var _j = 0; _j < uv.Length; ++_j ) {
                                        uv[ _j ].x *= r.lightmapScaleOffset.x;
                                        uv[ _j ].y *= r.lightmapScaleOffset.y;
                                        uv[ _j ].x += r.lightmapScaleOffset.z;
                                        uv[ _j ].y += r.lightmapScaleOffset.w;
                                        uv[ _j ].y = 1 - uv[ _j ].y;
                                        var _uv = uv[ _j ];
                                        if ( _uv.x < minx ) {
                                            minx = _uv.x;
                                        }
                                        if ( _uv.y < miny ) {
                                            miny = _uv.y;
                                        }
                                        if ( _uv.x > maxx ) {
                                            maxx = _uv.x;
                                        }
                                        if ( _uv.y > maxy ) {
                                            maxy = _uv.y;
                                        }
                                    }
                                    var bounds = new Vector4( minx, miny, maxx, maxy );
                                    litmapUVs.Add( new KeyValuePair<int, Vector4>( r.lightmapIndex, bounds ) );
                                    _LightmapUVBoundsCache.Add( r, bounds );
                                }
                            } else {
                                litmapUVs = litmapUVs ?? new List<KeyValuePair<int, Vector4>>();
                                litmapUVs.Add( new KeyValuePair<int, Vector4>( r.lightmapIndex, uvBounds ) );
                            }
                        }
                    }
                }
            }
            _litmapUVs = litmapUVs;
        }

        void OnSelectionChanged() {
            if ( m_window != null ) {
                m_window.Repaint();
                UpdateCache();
            }
        }

        static Vector2[] GetMeshUV2( Mesh mesh ) {
            Vector2[] ret = null;
            var assetPath = AssetDatabase.GetAssetPath( mesh );
            var ti = AssetImporter.GetAtPath( assetPath ) as ModelImporter;
            var id = String.Format( "{0}, {1}", assetPath, mesh.GetInstanceID() );
            if ( !String.IsNullOrEmpty( assetPath ) ) {
                if ( _MeshUVsCache.TryGetValue( id, out ret ) && ret != null ) {
                    return ret;
                }
                if ( mesh.isReadable == false ) {
                    if ( ti != null ) {
                        if ( ti.isReadable ) {
                            ret = mesh.uv2;
                            if ( ret.Length == 0 ) {
                                ret = mesh.uv;
                            }
                        } else {
                            try {
                                ti.isReadable = true;
                                AssetDatabase.ImportAsset( assetPath );
                                ret = mesh.uv2;
                                if ( ret.Length == 0 ) {
                                    ret = mesh.uv;
                                }
                            } finally {
                                ti.isReadable = false;
                                AssetDatabase.ImportAsset( assetPath );
                            }
                        }
                    }
                } else {
                    ret = mesh.uv2;
                    if ( ret.Length == 0 ) {
                        ret = mesh.uv;
                    }
                }
                _MeshUVsCache[ id ] = ret;
            }
            return ret;
        }

        Rect OnGUI_Lightmap() {
            Rect selectRect = new Rect();
            var lightmaps = LightmapSettings.lightmaps;
            if ( lightmaps != null && lightmaps.Length > 0 ) {
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField( "" );
                var width = 0.0f;
                var height = 0.0f;
                if ( Event.current.type == EventType.Repaint ) {
                    var rt = GUILayoutUtility.GetLastRect();
                    var x = rt.x;
                    var y = rt.y;
                    var gap = 4;
                    var border = x;
                    width = border * 2;
                    height = y + border;
                    var maxTexHeight = 0;
                    for ( int i = 0; i < lightmaps.Length; ++i ) {
                        if ( i > 0 ) {
                            width += gap;
                            x += gap;
                        }
                        var m = lightmaps[ i ];
                        var t = m.lightmapLight;
                        var t_width = t.width;
                        var t_height = t.height;
                        if ( t_width > 512 ) {
                            t_width >>= 1;
                        }
                        if ( t_height > 512 ) {
                            t_height >>= 1;
                        }
                        if ( t_height > maxTexHeight ) {
                            maxTexHeight = t_height;
                        }
                        if ( t != null ) {
                            EditorGUI.DrawPreviewTexture( new Rect( x, y, t_width, t_height ), t );
                            if ( _litmapUVs != null ) {
                                for ( int j = 0; j < _litmapUVs.Count; ++j ) {
                                    var uv = _litmapUVs[ j ];
                                    if ( uv.Key == i ) {
                                        var bounds = uv.Value;
                                        var a = Mathf.FloorToInt( x + bounds.x * t_width );
                                        var b = Mathf.FloorToInt( y + bounds.y * t_height );
                                        var c = Mathf.CeilToInt( ( bounds.z - bounds.x ) * t_width );
                                        var d = Mathf.CeilToInt( ( bounds.w - bounds.y ) * t_height );
                                        var color = Color.red;
                                        var _color = GUI.color;
                                        var rect = new Rect( a, b, c, d );
                                        var mouseOvered = rect.Contains( Event.current.mousePosition );
                                        color = mouseOvered ? Color.green : Color.red;
                                        color.a = mouseOvered ? 0.0f : 0.5f;
                                        GUI.color = color;
                                        EditorGUI.DrawTextureAlpha( new Rect( a, b, c, d ), Texture2D.whiteTexture, ScaleMode.StretchToFill );
                                        color = mouseOvered ? Color.green : Color.red;
                                        color.a = mouseOvered ? 0.5f : 0.0f;
                                        if ( mouseOvered ) {
                                            selectRect = new Rect( a, b, c, d );
                                        }
                                        GUI.color = color;
                                        EditorGUI.DrawRect( new Rect( a - 1, b - 1, c + 2, 1 ), color );
                                        EditorGUI.DrawRect( new Rect( a - 1, b, 1, d ), color );
                                        EditorGUI.DrawRect( new Rect( a - 1, b + d, c + 2, 1 ), color );
                                        EditorGUI.DrawRect( new Rect( a + c, b, 1, d ), color );
                                        GUI.color = _color;
                                    }
                                }
                            }
                            width += t_width;
                            x += t_width;
                        }
                    }
                    height += maxTexHeight;
                    if ( m_window != null ) {
                        var size = m_window.minSize;
                        if ( width > size.x ) {
                            size.x = width;
                        }
                        if ( height > size.y ) {
                            size.y = height;
                        }
                        m_window.minSize = size;
                    }
                }
                EditorGUILayout.LabelField(
                    String.Format( "PixelRect: ( {0}, {1}, {2}, {3} )",
                        selectRect.x, selectRect.y, selectRect.width, selectRect.height )
                );
                EditorGUILayout.EndVertical();
            }
            return selectRect;
        }

        void OnGUI() {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.ObjectField( "Current Selected:", Selection.activeGameObject, typeof( GameObject ), true );
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            OnGUI_Lightmap();
            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
        }
    }
}
