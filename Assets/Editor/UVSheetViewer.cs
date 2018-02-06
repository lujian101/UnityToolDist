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
using Common;

namespace AresEditor.ArtistKit {

    public class UVSheetViewer : EditorWindow {

        enum UVChannel {
            UV0,
            UV1,
            UV2,
            UV3,
        }

        class UVData {
            public String name = String.Empty;
            public List<int[]> subMeshes = null;
            public List<Vector2[]> uvs = new List<Vector2[]>();
            public UVData() {
                for ( int i = 0; i < 4; ++i ) {
                    uvs.Add( new Vector2[] { } );
                }
            }
        }

        static UVChannel m_selectUVChannel = UVChannel.UV0;
        static UVSheetViewer m_window = null;

        static int m_pageOffset = 0;
        static Dictionary<String, UVData> m_MeshUVCache = new Dictionary<String, UVData>();
        static List<KeyValuePair<UVData, Texture2D>> m_selectedUVData = null;
        static Texture2D _grayTexture = null;
        static Material _drawMatT = null;
        static Material _drawMatC = null;

        [MenuItem( "Tools/UVSheetViewer" )]
        static void Init() {
            if ( m_window == null ) {
                m_window = ( UVSheetViewer )EditorWindow.GetWindow( typeof( UVSheetViewer ), false, "UVSheet Viewer" );
            }
            m_window.minSize = new Vector2( 400, 300 );
            m_window.position = new Rect( 0, 0, m_window.minSize.x, m_window.minSize.y );
            m_window.Show();
        }

        void OnDestroy() {
            m_window = null;
            if ( _grayTexture != null ) {
                UnityEngine.Object.DestroyImmediate( _grayTexture );
                _grayTexture = null;
            }
            if ( _drawMatT != null ) {
                UnityEngine.Object.DestroyImmediate( _drawMatT );
                _drawMatT = null;
            }
            if ( _drawMatC != null ) {
                UnityEngine.Object.DestroyImmediate( _drawMatC );
                _drawMatC = null;
            }
        }

        void OnEnable() {
            Selection.selectionChanged += OnSelectionChanged;
            UpdateCache();
        }

        void OnDisable() {
            Selection.selectionChanged -= OnSelectionChanged;
            if ( m_MeshUVCache != null ) {
                m_MeshUVCache.Clear();
            }
        }

        void OnSelectionChanged() {
            if ( m_window != null ) {
                m_window.Repaint();
                UpdateCache();
                EditorApplication.delayCall += () => m_window.Repaint();
                m_window.Repaint();
            }
        }

        static String GetMeshAssetHash( String path, Mesh mesh ) {
            var assets = AssetDatabase.LoadAllAssetRepresentationsAtPath( path );
            var index = Array.IndexOf( assets, mesh );
            if ( path.EndsWith( ".asset" ) || assets.Length == 0 ) {
                index = 0;
            } else {
                UDebug.Assert( index >= 0 );
            }
            if ( File.Exists( path ) ) {
                return String.Format( "{0}-{1}", EditorUtils.Md5Asset( path ), index );
            } else {
                return String.Format( "{0}-{1}", path, mesh.GetInstanceID() );
            }
        }

        static UVData GetMeshUVData( Mesh mesh ) {
            var assetPath = AssetDatabase.GetAssetPath( mesh );
            UVData result = null;
            if ( String.IsNullOrEmpty( assetPath ) ) {
                result = null;
            } else {
                UVData md = null;
                var importer = AssetImporter.GetAtPath( assetPath ) as ModelImporter;
                var isReadable = true;
                var meshCompression = ModelImporterMeshCompression.Off;
                if ( importer != null ) {
                    isReadable = importer.isReadable;
                    meshCompression = importer.meshCompression;
                }
                Func<Mesh, UVData> readMeshData = _mesh => {
                    var _md = new UVData();
                    _md.name = _mesh.name;
                    _md.uvs[ 0 ] = _mesh.uv;
                    _md.uvs[ 1 ] = _mesh.uv2;
                    _md.uvs[ 2 ] = _mesh.uv3;
                    _md.uvs[ 3 ] = _mesh.uv4;
                    _md.subMeshes = new List<int[]>( _mesh.subMeshCount );
                    for ( int s = 0; s < _mesh.subMeshCount; ++s ) {
                        UDebug.Assert( _mesh.GetTopology( s ) == MeshTopology.Triangles );
                        _md.subMeshes.Add( _mesh.GetTriangles( s ) );
                    }
                    return _md;
                };
                UVData _cachedMeshData = null;
                var key = GetMeshAssetHash( assetPath, mesh );
                if ( m_MeshUVCache.TryGetValue( key, out _cachedMeshData ) ) {
                    md = _cachedMeshData;
                } else {
                    if ( !isReadable || meshCompression != ModelImporterMeshCompression.Off ) {
                        if ( importer != null ) {
                            importer.isReadable = true;
                            importer.meshCompression = ModelImporterMeshCompression.Off;
                            AssetDatabase.ImportAsset( assetPath );
                        }
                    }
                    try {
                        md = readMeshData( mesh );
                    } finally {
                        if ( !isReadable || meshCompression != ModelImporterMeshCompression.Off ) {
                            if ( importer != null ) {
                                importer.isReadable = isReadable;
                                importer.meshCompression = meshCompression;
                                AssetDatabase.ImportAsset( assetPath );
                            }
                        }
                        if ( md != null ) {
                            m_MeshUVCache.Add( key, md );
                        }
                    }
                }
                result = md;
            }
            return result;
        }

        static void UpdateCache() {
            var objs = Selection.objects;
            var meshes = new HashSet<Mesh>();
            var dict = new Dictionary<Mesh, Texture2D>();
            for ( int i = 0; i < objs.Length; ++i ) {
                var go = objs[ i ] as GameObject;
                if ( go != null ) {
                    var rs = go.GetComponentsInChildren<Renderer>();
                    for ( int j = 0; j < rs.Length; ++j ) {
                        var r = rs[ j ];
                        if ( r != null ) {
                            Mesh m = null;
                            Material mat = null;
                            if ( r is MeshRenderer ) {
                                var mf = r.GetComponent<MeshFilter>();
                                if ( mf != null ) {
                                    m = mf.sharedMesh;
                                }
                                var mr = r.GetComponent<MeshRenderer>();
                                if ( mr != null ) {
                                    mat = mr.sharedMaterial;
                                }
                            } else if ( r is SkinnedMeshRenderer ) {
                                var _r = r as SkinnedMeshRenderer;
                                m = _r.sharedMesh;
                                if ( _r != null ) {
                                    mat = _r.sharedMaterial;
                                }
                            }
                            if ( m != null ) {
                                if ( mat != null && mat.HasProperty( "_MainTex" ) ) {
                                    dict[ m ] = mat.mainTexture as Texture2D;
                                }
                                meshes.Add( m );
                            }
                        }
                    }
                } else if ( objs[ i ] is Mesh ) {
                    meshes.Add( objs[ i ] as Mesh );
                }
            }
            m_selectedUVData = new List<KeyValuePair<UVData, Texture2D>>();
            try {
                int i = 0;
                foreach ( var mesh in meshes ) {
                    var md = GetMeshUVData( mesh );
                    if ( EditorUtility.DisplayCancelableProgressBar( "UVSheetViewer", mesh.name, ( float )i / meshes.Count ) ) {
                        break;
                    }
                    if ( md != null ) {
                        Texture2D hintTex = null;
                        dict.TryGetValue( mesh, out hintTex );
                        m_selectedUVData.Add( new KeyValuePair<UVData, Texture2D>( md, hintTex ?? Texture2D.blackTexture ) );
                    }
                }
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        void OnGUI() {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.Space();
            EditorGUILayout.ObjectField( "Current Selected:", Selection.activeGameObject, typeof( GameObject ), true );
            m_selectUVChannel = ( UVChannel )EditorGUILayout.EnumPopup( "UVChannel", m_selectUVChannel );
            EditorGUILayout.Space();
            OnGUI_DrawUVSheets();
            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
        }

        void OnGUI_DrawUVSheets() {
            const int MaxPage = 3;
            const int SheetWidth = 512;
            const int SheetHeight = 512;
            const int uvWindowOffsetX = 8;
            const int uvWindowOffsetY = 8;
            const int uvWindowWidth = SheetWidth - uvWindowOffsetX * 2;
            const int uvWindowHeight = SheetHeight - uvWindowOffsetY * 2;
            if ( _grayTexture == null ) {
                _grayTexture = new Texture2D( 1, 1 );
                _grayTexture.SetPixel( 0, 0, Color.grey );
            }
            var uvs = m_selectedUVData;
            if ( uvs != null && uvs.Count > 0 ) {
                EditorGUILayout.BeginVertical();
                m_pageOffset = EditorGUILayout.IntSlider( "Page", m_pageOffset, 0, Math.Max( 0, uvs.Count - MaxPage ) );
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
                    for ( int i = m_pageOffset; i < uvs.Count; ++i ) {
                        if ( i > m_pageOffset && ( i - m_pageOffset ) < MaxPage ) {
                            width += gap;
                            x += gap;
                        }
                        var m = m_selectedUVData[ i ];
                        if ( SheetHeight > maxTexHeight ) {
                            maxTexHeight = SheetHeight;
                        }
                        EditorGUI.DrawTextureAlpha( new Rect( x, y, SheetWidth, SheetHeight ), _grayTexture );
                        var uv_rt = new Rect( x + uvWindowOffsetX, y + uvWindowOffsetY, uvWindowWidth, uvWindowHeight );
                        var inBounds = DrawSheet( uv_rt, m.Key, m_selectUVChannel == UVChannel.UV0, m.Value );
                        GUI.color = Color.black;
                        EditorGUI.LabelField( new Rect( x + uvWindowOffsetX + 1, y + uvWindowOffsetY + 2, 120, 32 ), m.Key.name );
                        GUI.color = inBounds ? Color.green : Color.red;
                        EditorGUI.LabelField( new Rect( x + uvWindowOffsetX, y + uvWindowOffsetY, 120, 32 ), m.Key.name );
                        
                        if ( ( i - m_pageOffset ) < MaxPage ) {
                            width += SheetWidth;
                        }
                        x += SheetWidth;
                    }
                    GUI.color = Color.white;

                    height += maxTexHeight;
                    if ( m_window != null ) {
                        var size = m_window.minSize;
                        if ( width != size.x ) {
                            size.x = width;
                        }
                        if ( height != size.y ) {
                            size.y = height;
                        }
                        m_window.minSize = size;
                        m_window.maxSize = size;
                    }
                }
                EditorGUILayout.EndVertical();
            }
        }
        
        static float ClampRepeatUV( float value, ref bool modified ) {
            if ( Mathf.Abs( value ) < 1e-6f ) {
                value = 0;
            }
            if ( Mathf.Abs( 1 - value ) < 1e-6f ) {
                value = 1;
            }
            if ( value < 0 || value > 1 ) {
                var _value = value % 1.0f;
                if ( _value < 0.0 ) {
                    _value += 1.0f;
                }
                modified = _value != value;
                return _value;
            } else {
                modified = false;
                return value;
            }
        }

        bool DrawSheet( Rect rect, UVData uvset, bool withBG = false, Texture2D tex = null ) {
            var result = true;
            if ( _drawMatT == null ) {
                var shader = Shader.Find( "Unlit/Texture" );
                _drawMatT = new Material( shader );
            }
            if ( _drawMatC == null ) {
                var shader = Shader.Find( "Hidden/Internal-Colored" );
                _drawMatC = new Material( shader );
            }
            if ( Event.current.type == EventType.Repaint ) {
                GUI.BeginClip( rect );
                GL.PushMatrix();

                GL.Clear( true, false, Color.black );
                // background
                if ( withBG && tex != null && _drawMatT.HasProperty( "_MainTex" ) ) {
                    _drawMatT.mainTexture = tex;
                } else {
                    _drawMatT.mainTexture = Texture2D.blackTexture;
                }
                _drawMatT.SetPass( 0 );
                GL.Begin( GL.QUADS );
                GL.Color( Color.black );
                GL.TexCoord2( 0, 1 );
                GL.Vertex3( 0, 0, 0 );
                GL.TexCoord2( 1, 1 );
                GL.Vertex3( rect.width, 0, 0 );
                GL.TexCoord2( 1, 0 );
                GL.Vertex3( rect.width, rect.height, 0 );
                GL.TexCoord2( 0, 0 );
                GL.Vertex3( 0, rect.height, 0 );
                GL.End();

                _drawMatC.SetPass( 0 );
                // draw grid
                GL.Begin( GL.LINES );
                int count = 25;
                var sx = 0;
                var sy = 0;
                var dx = ( float )rect.width / count;
                var dy = ( float )rect.height / count;
                for ( int i = 0; i <= count; ++i ) {
                    float f = ( i % 5 == 0 ) ? 0.5f : 0.2f;
                    GL.Color( new Color( f, f, f, 1 ) );
                    sx = ( int )Math.Ceiling( dx * i );
                    sy = ( int )Math.Ceiling( dy * i );
                    GL.Vertex3( sx, 0, 0 );
                    GL.Vertex3( sx, rect.height, 0 );
                    GL.Vertex3( 0, sy, 0 );
                    GL.Vertex3( rect.width, sy, 0 );
                }
                GL.End();

                var scale = new Vector2( rect.width, rect.height );
                var subMeshes = uvset.subMeshes;
                var uv = uvset.uvs[ ( int )m_selectUVChannel ];
                if ( uv != null && uv.Length > 0 ) {
                    GL.Begin( GL.LINES );
                    GL.Color( Color.green );
                    GL.TexCoord2( 0, 0 );
                    for ( int j = 0; j < subMeshes.Count; ++j ) {
                        var tri = subMeshes[ j ];
                        for ( int t = 0; t < tri.Length; t += 3 ) {
                            var pt0 = uv[ tri[ t ] ];
                            var pt1 = uv[ tri[ t + 1 ] ];
                            var pt2 = uv[ tri[ t + 2 ] ];
                            var b = false;
                            if ( pt0.x < 0 || pt0.x > 1 ) {
                                result = false;
                            }
                            if ( pt0.y < 0 || pt0.y > 1 ) {
                                result = false;
                            }
                            if ( pt1.x < 0 || pt1.x > 1 ) {
                                result = false;
                            }
                            if ( pt1.y < 0 || pt1.y > 1 ) {
                                result = false;
                            }
                            if ( pt2.x < 0 || pt2.x > 1 ) {
                                result = false;
                            }
                            if ( pt2.y < 0 || pt2.y > 1 ) {
                                result = false;
                            }
                            pt0.x = ClampRepeatUV( pt0.x, ref b );
                            pt0.y = ClampRepeatUV( pt0.y, ref b );
                            pt1.x = ClampRepeatUV( pt1.x, ref b );
                            pt1.y = ClampRepeatUV( pt1.y, ref b );
                            pt2.x = ClampRepeatUV( pt2.x, ref b );
                            pt2.y = ClampRepeatUV( pt2.y, ref b );
                            pt0.y = 1 - pt0.y;
                            pt1.y = 1 - pt1.y;
                            pt2.y = 1 - pt2.y;
                            pt0.Scale( scale );
                            pt1.Scale( scale );
                            pt2.Scale( scale );
                            if ( b ) {
                                GL.Color( Color.red );
                            } else {
                                GL.Color( Color.green );
                            }
                            GL.Vertex3( pt0.x, pt0.y, 0 );
                            GL.Vertex3( pt1.x, pt1.y, 0 );
                            GL.Vertex3( pt1.x, pt1.y, 0 );
                            GL.Vertex3( pt2.x, pt2.y, 0 );
                            GL.Vertex3( pt2.x, pt2.y, 0 );
                            GL.Vertex3( pt0.x, pt0.y, 0 );
                        }
                    }
                    GL.End();
                }

                GL.PopMatrix();

                GUI.EndClip();
            }
            return result;
        }
    }
}
