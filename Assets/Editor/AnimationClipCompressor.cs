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

#pragma warning disable 0618

namespace AresEditor.ArtistKit {

    internal class AnimationClipCompressor : EditorWindow {

        internal class Arg {
            internal const float Default_PositionError = 0.01f;
            internal const float Default_RotationError = 0.05f;
            internal const float Default_ScaleError = 0.01f;
            internal const float Default_DepthScale = 1.125f;

            internal float positionError = Default_PositionError;
            internal float rotationError = Default_RotationError;
            internal float scaleError = Default_ScaleError;
            internal float depthScale = Default_DepthScale;
            internal bool removeScaleCurve = false;
            internal Arg Clone() {
                return new Arg() {
                    positionError = this.positionError,
                    rotationError = this.rotationError,
                    scaleError = this.scaleError,
                    depthScale = this.depthScale,
                    removeScaleCurve = this.removeScaleCurve,
                };
            }
            internal void CopyFrom( Arg args ) {
                positionError = args.positionError;
                rotationError = args.rotationError;
                scaleError = args.scaleError;
                depthScale = args.depthScale;
                removeScaleCurve = args.removeScaleCurve;
            }
            public override String ToString() {
                return String.Format(
                    "p = {0}, r = {1}, s = {2}, d = {3}, rms = {4}",
                    positionError.ToString(),
                    rotationError.ToString(),
                    scaleError.ToString(),
                    depthScale,
                    removeScaleCurve.ToString().ToLower()
                );
            }
            public void Reset() {
                positionError = Default_PositionError;
                rotationError = Default_RotationError;
                scaleError = Default_ScaleError;
                depthScale = Default_DepthScale;
                removeScaleCurve = false;
            }
        }

        internal class ClipInfo {
            internal bool selected = false;
            internal bool expand = false;
            internal String title = String.Empty;
            internal String path = String.Empty;
            internal int size = 0;
            internal int compressed_size = -1;
            internal int refCount = 0;
            internal int sizePerSec = 0;
            internal AnimationClip _clip = null;
            internal AssetImporter _importer = null;
            internal String _args = String.Empty;
            internal Arg _arg = null;
            internal AnimationClip clip {
                get {
                    if ( _clip == null && !String.IsNullOrEmpty( path ) ) {
                        _clip = AssetDatabase.LoadAssetAtPath( path, typeof( AnimationClip ) ) as AnimationClip;
                    }
                    return _clip;
                }
                set {
                    _clip = value;
                }
            }
            internal void InitArg() {
                AssetImporter importer = null;
                if ( _clip != null ) {
                    importer = AssetImporter.GetAtPath( AssetDatabase.GetAssetPath( _clip ) );
                }
                _arg = null;
                _args = String.Empty;
                if ( _importer == null && importer != null ) {
                    _importer = importer;
                    if ( !String.IsNullOrEmpty( importer.userData ) ) {
                        var root = JSONObject.Create( importer.userData );
                        _arg = new AnimationClipCompressor.Arg();
                        root.GetField( out _arg.positionError, "p", _arg.positionError );
                        root.GetField( out _arg.rotationError, "r", _arg.rotationError );
                        root.GetField( out _arg.scaleError, "s", _arg.scaleError );
                        root.GetField( out _arg.depthScale, "d", _arg.depthScale );
                        root.GetField( out _arg.removeScaleCurve, "rms", _arg.removeScaleCurve );
                        _args = _arg.ToString();
                    }
                }
            }
            internal void Apply() {
                if ( _importer != null ) {
                    var s = _importer.userData;
                    if ( _arg != null ) {
                        var root = new JSONObject( JSONObject.Type.OBJECT );
                        root.SetField( "p", _arg.positionError );
                        root.SetField( "r", _arg.rotationError );
                        root.SetField( "s", _arg.scaleError );
                        root.SetField( "d", _arg.depthScale );
                        root.SetField( "rms", _arg.removeScaleCurve );
                        _importer.userData = root.ToString( true );
                    } else {
                        _importer.userData = String.Empty;
                    }
                    if ( _importer.userData != s ) {
                        _importer.SaveAndReimport();
                    }
                }
            }
            internal void Reset() {
                _arg = null;
                _args = String.Empty;
                if ( _importer != null ) {
                    var s = _importer.userData;
                    if ( !String.IsNullOrEmpty( s ) ) {
                        _importer.userData = String.Empty;
                        _importer.SaveAndReimport();
                    }
                }
            }
        }

        internal class Overall {
            internal int totalSize = 0;
            internal int compressedSize = 0;
            internal int savedSize = 0;
        }

        enum SortType {
            Name,
            Size,
        }
        static readonly String[] SortOptions = new String[] { "Name", "Size" };

        const int WarningSize = 1 << 18;

        static AnimationClipCompressor m_window = null;
        static Vector2 m_argsViewPos = Vector2.zero;
        static Vector2 m_clipsViewPos = Vector2.zero;
        static Dictionary<String, ClipInfo> m_selectedAnimationClips = new Dictionary<String, ClipInfo>();
        static List<ClipInfo> m_sortedAnimations = new List<ClipInfo>();
        static int m_selectedCount = 0;
        static String[] m_animName = null;
        static SortType m_animListSortType = SortType.Name;
        static Overall m_overall = new Overall();
        static Arg m_argEditing = new AnimationClipCompressor.Arg();

        [MenuItem( "Tools/AnimationClip Compressor" )]
        static void Init() {
            if ( m_window == null ) {
                m_window = EditorWindow.GetWindow<AnimationClipCompressor>( "AnimationClip Compressor", true, typeof( EditorWindow ) );
            }
            m_window.minSize = new Vector2( 600, 300 );
            m_window.Show();
        }

        static Dictionary<String, ClipInfo> Scan() {
            var o = Selection.objects;
            var assets = new HashSet<String>();
            var ret = new Dictionary<String, ClipInfo>();
            var prefabInstanceSelected = false;
            for ( int i = 0; i < o.Length; ++i ) {
                var path = AssetDatabase.GetAssetPath( o[ i ] );
                if ( !String.IsNullOrEmpty( path ) ) {
                    assets.Add( path );
                } else {
                    var prefab = PrefabUtility.GetPrefabParent( o[ i ] );
                    if ( prefab ) {
                        path = AssetDatabase.GetAssetPath( prefab );
                        if ( !String.IsNullOrEmpty( path ) ) {
                            assets.Add( path );
                            prefabInstanceSelected = true;
                        }
                    }
                }
            }
            if ( !prefabInstanceSelected ) {
                var pathList = EditorUtils.GetAllSelectedPath();
                if ( pathList != null && pathList.Count > 0 ) {
                    for ( int i = 0; i < pathList.Count; ++i ) {
                        if ( System.IO.Directory.Exists( pathList[ i ] ) ) {
                            var fs = FileUtils.GetFileList( pathList[ i ], EditorUtils.FileFilter_prefab );
                            for ( int j = 0; j < fs.Count; ++j ) {
                                var assetPath = EditorUtils.RelateToAssetsPath( fs[ j ] );
                                assets.Add( assetPath );
                            }
                        }
                    }
                }
            }
            var list = assets.ToList();
            list.Sort();
            try {
                var _InputRoot = "^" + AnimationClipCompressImp.InputRoot;
                var par = new String[ 1 ] { null };
                for ( int i = 0; i < list.Count; ++i ) {
                    if ( EditorUtility.DisplayCancelableProgressBar( "Animation Clip Collecting" + new String( '.', Utils.GetSystemTicksSec() % 3 ), list[ i ], ( float )i / list.Count ) ) {
                        break;
                    }
                    par[ 0 ] = list[ i ];
                    var deps = AssetDatabase.GetDependencies( par ).Where( n => n.EndsWith( ".anim" ) ).ToList();
                    for ( int j = 0; j < deps.Count; ++j ) {
                        ClipInfo ci;
                        if ( !ret.TryGetValue( deps[ j ], out ci ) ) {
                            ci = new ClipInfo();
                            var n = deps[ j ];
                            var pos = -2;
                            for ( int k = n.Length - 1; k >= 0; --k ) {
                                if ( n[ k ] == '/' ) {
                                    if ( pos < -1 ) {
                                        pos++;
                                    } else {
                                        pos = k;
                                        break;
                                    }
                                }
                            }
                            if ( pos >= 0 ) {
                                n = "..." + n.Substring( pos, n.Length - pos - ".anim".Length );
                            }
                            ci.title = n;
                            var assetPath = deps[ j ];
                            var outAssetPath = String.Empty;
                            if ( assetPath.StartsWith( AnimationClipCompressImp.OutputRoot ) ) {
                                var srcAssetPath = assetPath.Replace( AnimationClipCompressImp.OutputRoot, AnimationClipCompressImp.InputRoot );
                                outAssetPath = assetPath;
                                assetPath = srcAssetPath;
                            } else {
                                outAssetPath = Regex.Replace( assetPath, _InputRoot, AnimationClipCompressImp.OutputRoot );
                            }
                            ci.path = assetPath;
                            ci.clip = AssetDatabase.LoadAssetAtPath( assetPath, typeof( AnimationClip ) ) as AnimationClip;
                            
#if UNITY_5
                            var inFile = ci.clip;
                            var outFile = AssetDatabase.LoadAssetAtPath( outAssetPath, typeof( AnimationClip ) ) as AnimationClip;
                            ci.size = outFile ? UnityEngine.Profiling.Profiler.GetRuntimeMemorySize( outFile ) : ( inFile ? UnityEngine.Profiling.Profiler.GetRuntimeMemorySize( inFile ) : -1 );
                            ci.compressed_size = outFile && inFile ? UnityEngine.Profiling.Profiler.GetRuntimeMemorySize( inFile ) : -1;
#else
                            var inFileInfo = new FileInfo( assetPath );
                            var outFileInfo = new FileInfo( outAssetPath );
                            ci.size = outFileInfo.Exists ? ( int )outFileInfo.Length : ( inFileInfo.Exists ? ( int )inFileInfo.Length : -1 );
                            ci.compressed_size = outFileInfo.Exists && inFileInfo.Exists ? ( int )inFileInfo.Length : -1;
#endif
                            ci.sizePerSec = ( int )( ci.size / ci.clip.length );
                            ci.InitArg();
                            ret.Add( deps[ j ], ci );
                        }
                        ci.refCount++;
                    }
                }
            } finally {
                EditorUtility.ClearProgressBar();
            }
            return ret;
        }

        static String[] TakeAnimNames( List<ClipInfo> clips, int maxCount = -1 ) {
            var set = new Dictionary<String, int>();
            String[] ret = null;
            for ( int i = 0; i < clips.Count; ++i ) {
                var name = clips[ i ].clip.name;
                if ( set.ContainsKey( name ) ) {
                    set[ name ] = set[ name ] + 1;
                } else {
                    set[ name ] = 1;
                }
            }
            var sorter = new List<KeyValuePair<String, int>>();
            foreach ( var item in set ) {
                sorter.Add( item );
            }
            sorter.Sort(
                ( l, r ) => {
                    var c = r.Value.CompareTo( l.Value );
                    if ( c == 0 ) {
                        c = l.Key.CompareTo( r.Key );
                    }
                    return c;
                }
            );
            if ( maxCount <= 0 ) {
                maxCount = sorter.Count;
            } else {
                maxCount = Math.Min( maxCount, sorter.Count );
            }
            ret = new String[ maxCount ];
            for ( int i = 0; i < maxCount; ++i ) {
                ret[ i ] = sorter[ i ].Key;
            }
            return ret;
        }

        static void SortClips( List<ClipInfo> clips, SortType type ) {
            switch ( type ) {
            case SortType.Name:
                clips.Sort(
                    ( l, r ) => {
                        var c = l.title.CompareTo( r.title );
                        if ( c == 0 ) {
                            c = r.size.CompareTo( l.size );
                            if ( c == 0 ) {
                                c = r.sizePerSec.CompareTo( l.sizePerSec );
                            }
                        }
                        return c;
                    }
                );
                break;
            case SortType.Size:
                clips.Sort(
                    ( l, r ) => {
                        var c = r.size.CompareTo( l.size );
                        if ( c == 0 ) {
                            c = r.sizePerSec.CompareTo( l.sizePerSec );
                            if ( c == 0 ) {
                                c = l.title.CompareTo( r.title );
                            }
                        }
                        return c;
                    }
                );
                break;
            }
        }

        static void UpdateOverall() {
            m_overall.totalSize = 0;
            m_overall.compressedSize = 0;
            m_overall.savedSize = 0;
            if ( m_sortedAnimations != null ) {
                for ( int i = 0; i < m_sortedAnimations.Count; ++i ) {
                    var ci = m_sortedAnimations[ i ];
                    m_overall.totalSize += ci.size;
                    m_overall.compressedSize += ci.compressed_size > 0 ? ci.compressed_size : ci.size;
                }
                m_overall.savedSize = m_overall.totalSize - m_overall.compressedSize;
            }
        }

        void OnGUI() {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            if ( m_selectedAnimationClips.Count == 0 ) {
                if ( GUILayout.Button( "Open" ) ) {
                    m_selectedAnimationClips = Scan();
                    m_sortedAnimations = m_selectedAnimationClips.Values.ToList();
                    SortClips( m_sortedAnimations, m_animListSortType );
                    m_animName = TakeAnimNames( m_sortedAnimations );
                    m_selectedCount = 0;
                    m_overall = new Overall();
                    UpdateOverall();
                }
            } else {
                if ( GUILayout.Button( "Close" ) ) {
                    m_selectedAnimationClips.Clear();
                    m_sortedAnimations.Clear();
                    m_selectedCount = 0;
                }
            }
            EditorGUILayout.EndHorizontal();
            if ( m_selectedAnimationClips.Count > 0 ) {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.Separator();
                    EditorGUILayout.BeginVertical();
                    var type = ( SortType )GUILayout.Toolbar( ( int )m_animListSortType, SortOptions, GUILayout.MaxWidth( 500 ) );
                    EditorGUILayout.LabelField(
                        String.Format( "size = {0}, compressed = {1}, saved = {2}",
                            StringUtils.FormatMemorySize( m_overall.totalSize ),
                            StringUtils.FormatMemorySize( m_overall.compressedSize ),
                            StringUtils.FormatMemorySize( m_overall.savedSize )
                        )
                    );
                    if ( type != m_animListSortType ) {
                        m_animListSortType = type;
                        SortClips( m_sortedAnimations, m_animListSortType );
                        m_animName = TakeAnimNames( m_sortedAnimations );
                    }
                    EditorGUILayout.Separator();
                    m_clipsViewPos = EditorGUILayout.BeginScrollView( m_clipsViewPos, GUILayout.MaxWidth( 500 ) );
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.BeginVertical( GUILayout.MaxWidth( 320 ) );
                            for ( int i = 0; i < m_sortedAnimations.Count; ++i ) {
                                EditorGUILayout.BeginHorizontal();
                                var ci = m_sortedAnimations[ i ];
                                var newstate = EditorGUILayout.ToggleLeft( "", ci.selected, GUILayout.Width( 16 ) );
                                if ( newstate != ci.selected ) {
                                    ci.selected = newstate;
                                    if ( Event.current.shift ) {
                                        m_sortedAnimations.ForEach( _ci => _ci.selected = newstate );
                                    } else if ( Event.current.control ) {
                                        m_sortedAnimations.ForEach(
                                            _ci => {
                                                if ( LevenshteinDistance.LevenshteinDistancePercent( ci.clip.name, _ci.clip.name ) > 0.6 ) {
                                                    _ci.selected = newstate;
                                                }
                                            }
                                        );
                                    }
                                    m_selectedCount = m_sortedAnimations.Count( _ci => _ci.selected );
                                }
                                var color = GUI.color;
                                if ( ci.size > WarningSize ) {
                                    GUI.color = Color.yellow;
                                } else {
                                    GUI.color = Color.white;
                                }
                                EditorGUILayout.BeginVertical();
                                var expand = EditorGUILayout.Foldout( ci.expand, ci.title );
                                if ( ci.expand != expand ) {
                                    ci.expand = expand;
                                    m_argEditing = ci._arg != null ? ci._arg.Clone() : new Arg();
                                    if ( expand ) {
                                        var curI = i;
                                        for ( int _i = 0; _i < m_sortedAnimations.Count; ++_i ) {
                                            if ( curI != _i ) {
                                                var _ci = m_sortedAnimations[ _i ];
                                                _ci.expand = false;
                                            }
                                        }
                                    }
                                }
                                GUI.color = color;
                                if ( ci.expand ) {
                                    EditorGUILayout.BeginHorizontal();
                                    if ( GUILayout.Button( "Apply", GUILayout.Width( 50 ) ) ) {
                                        ci._arg = m_argEditing.Clone();
                                        ci._args = ci._arg.ToString();
                                        ci.Apply();
                                    }
                                    GUI.enabled = ci._arg != null;
                                    if ( GUILayout.Button( "Reset", GUILayout.Width( 50 ) ) ) {
                                        ci.Reset();
                                    }
                                    GUI.enabled = true;
                                    EditorGUILayout.LabelField( String.IsNullOrEmpty( ci._args ) ? "default" : ci._args );
                                    EditorGUILayout.EndHorizontal();
                                }
                                EditorGUILayout.EndVertical();
                                EditorGUILayout.EndHorizontal();
                            }
                            EditorGUILayout.EndVertical();

                            EditorGUILayout.Separator();

                            EditorGUILayout.BeginVertical();
                            for ( int i = 0; i < m_sortedAnimations.Count; ++i ) {
                                var ci = m_sortedAnimations[ i ];
                                EditorGUILayout.LabelField( String.Format( "{0} / {1}", StringUtils.FormatMemorySize( ci.size ), ci.compressed_size > 0 ? StringUtils.FormatMemorySize( ci.compressed_size ) : "--" ) );
                                if ( ci.expand ) {
                                    EditorGUILayout.LabelField( String.Empty );
                                }
                            }
                            EditorGUILayout.EndVertical();
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                    EditorGUILayout.Space();
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.Separator();
                {
                    EditorGUILayout.BeginVertical();
                    for ( int i = 0; i < m_sortedAnimations.Count; ++i ) {
                        var ci = m_sortedAnimations[ i ];
                        if ( ci.expand ) {
                            {
                                var color = GUI.color;
                                var args = String.Empty;
                                EditorGUILayout.BeginVertical();
                                {
                                    EditorGUILayout.BeginHorizontal();
                                    GUI.color = m_argEditing.positionError > 0 ? Color.white : Color.gray;
                                    m_argEditing.positionError = EditorGUILayout.FloatField( "Position Error:", m_argEditing.positionError );
                                    m_argEditing.positionError = Mathf.Clamp( m_argEditing.positionError, 0, 1 );
                                    if ( GUILayout.Button( "R", GUILayout.Width( 20 ) ) ) {
                                        m_argEditing.positionError = Arg.Default_PositionError;
                                        Repaint();
                                    }
                                    EditorGUILayout.EndHorizontal();
                                    
                                    EditorGUILayout.BeginHorizontal();
                                    GUI.color = m_argEditing.rotationError > 0 ? Color.white : Color.gray;
                                    m_argEditing.rotationError = EditorGUILayout.FloatField( "Rotation Error:", m_argEditing.rotationError );
                                    m_argEditing.rotationError = Mathf.Clamp( m_argEditing.rotationError, 0, 1 );
                                    if ( GUILayout.Button( "R", GUILayout.Width( 20 ) ) ) {
                                        m_argEditing.rotationError = Arg.Default_RotationError;
                                        Repaint();
                                    }
                                    EditorGUILayout.EndHorizontal();

                                    EditorGUILayout.BeginHorizontal();
                                    GUI.color = m_argEditing.scaleError > 0 ? Color.white : Color.gray;
                                    m_argEditing.scaleError = EditorGUILayout.FloatField( "Scale Error:", m_argEditing.scaleError );
                                    m_argEditing.scaleError = Mathf.Clamp( m_argEditing.scaleError, 0, 1 );
                                    if ( GUILayout.Button( "R", GUILayout.Width( 20 ) ) ) {
                                        m_argEditing.scaleError = Arg.Default_ScaleError;
                                        Repaint();
                                    }
                                    EditorGUILayout.EndHorizontal();

                                    EditorGUILayout.BeginHorizontal();
                                    GUI.color = color;
                                    m_argEditing.depthScale = EditorGUILayout.FloatField( "Depth Scale:", m_argEditing.depthScale );
                                    m_argEditing.depthScale = Math.Max( m_argEditing.depthScale, 1 );
                                    m_argEditing.depthScale = Mathf.Clamp( m_argEditing.depthScale, 1, 2 );
                                    if ( GUILayout.Button( "R", GUILayout.Width( 20 ) ) ) {
                                        m_argEditing.depthScale = Arg.Default_DepthScale;
                                        Repaint();
                                    }
                                    EditorGUILayout.EndHorizontal();

                                    m_argEditing.removeScaleCurve = EditorGUILayout.Toggle( "Remove Scale Curve", m_argEditing.removeScaleCurve );
                                    args = m_argEditing.ToString();
                                    EditorGUILayout.BeginVertical();
                                    EditorGUILayout.BeginHorizontal();
                                    if ( GUILayout.Button( "Apply Selected" ) ) {
                                        for ( int j = 0; j < m_sortedAnimations.Count; ++j ) {
                                            if ( m_sortedAnimations[ j ].selected ) {
                                                m_sortedAnimations[ j ]._arg = m_argEditing.Clone();
                                                m_sortedAnimations[ j ]._args = args;
                                                m_sortedAnimations[ j ].Apply();
                                            }
                                        }
                                    }
                                    if ( GUILayout.Button( "Apply All" ) ) {
                                        for ( int j = 0; j < m_sortedAnimations.Count; ++j ) {
                                            m_sortedAnimations[ j ]._arg = m_argEditing.Clone();
                                            m_sortedAnimations[ j ]._args = args;
                                            m_sortedAnimations[ j ].Apply();
                                        }
                                    }
                                    EditorGUILayout.EndHorizontal();
                                    EditorGUILayout.BeginHorizontal();
                                    if ( GUILayout.Button( "Reset Selected" ) ) {
                                        for ( int j = 0; j < m_sortedAnimations.Count; ++j ) {
                                            if ( m_sortedAnimations[ j ].selected ) {
                                                m_sortedAnimations[ j ].Reset();
                                            }
                                        }
                                    }
                                    if ( GUILayout.Button( "Reset All" ) ) {
                                        for ( int j = 0; j < m_sortedAnimations.Count; ++j ) {
                                            m_sortedAnimations[ j ].Reset();
                                        }
                                    }
                                    EditorGUILayout.EndHorizontal();
                                    EditorGUILayout.EndVertical();
                                }
                                EditorGUILayout.EndVertical();
                            }
                            break;
                        }
                    }
                    EditorGUILayout.Separator();
                    if ( GUILayout.Button( "<< Compress >>" ) ) {
                        DoCompress();
                        UpdateOverall();
                    }
                    EditorGUILayout.HelpBox( String.Format( "Total AnimationClip Count: {0}, Selected: {1}", m_sortedAnimations != null ? m_sortedAnimations.Count : 0, m_selectedCount ), MessageType.Info );
                    EditorGUILayout.Separator();
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.Separator();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        static void DoCompress() {
            if ( m_sortedAnimations != null ) {
                AnimationClipCompressImp.ExportAll( m_sortedAnimations );
            }
        }

        static void SaveOnly() {
            if ( m_sortedAnimations != null ) {
                AnimationClipCompressImp.ExportAll( m_sortedAnimations, true );
            }
        }
    }

    internal static class LevenshteinDistance {

        static private int LowerOfThree( int first, int second, int third ) {
            int min = Math.Min( first, second );
            return Math.Min( min, third );
        }

        static private int Levenshtein_Distance( string str1, string str2 ) {
            int[ , ] Matrix;
            int n = str1.Length;
            int m = str2.Length;
            int temp = 0;
            char ch1;
            char ch2;
            int i = 0;
            int j = 0;
            if ( n == 0 ) {
                return m;
            }
            if ( m == 0 ) {
                return n;
            }
            Matrix = new int[ n + 1, m + 1 ];

            for ( i = 0; i <= n; i++ ) {
                Matrix[ i, 0 ] = i;
            }

            for ( j = 0; j <= m; j++ ) {
                Matrix[ 0, j ] = j;
            }

            for ( i = 1; i <= n; i++ ) {
                ch1 = str1[ i - 1 ];
                for ( j = 1; j <= m; j++ ) {
                    ch2 = str2[ j - 1 ];
                    if ( ch1.Equals( ch2 ) ) {
                        temp = 0;
                    } else {
                        temp = 1;
                    }
                    Matrix[ i, j ] = LowerOfThree( Matrix[ i - 1, j ] + 1, Matrix[ i, j - 1 ] + 1, Matrix[ i - 1, j - 1 ] + temp );
                }
            }
            return Matrix[ n, m ];
        }

        public static float LevenshteinDistancePercent( string str1, string str2 ) {
            int val = Levenshtein_Distance( str1, str2 );
            return 1 - ( float )val / Math.Max( str1.Length, str2.Length );
        }
    }

    internal class CurveCompressor {

        public static float m_positionError = 0;
        public static float m_rotationError = 0;
        public static float m_scaleError = 0;
        public static float m_depthScale = 1;
        public static bool m_removeScaleCurve = false;

        const float TangentEpsilon = 1e-4f;

        class KeyframeSample {
            public enum Type {
                Position,
                Rotation,
                Scale,
                Other,
            }
            public Keyframe keyframe;
            public Vector2 pos;
        }

        static List<Keyframe> __keyframes = new List<Keyframe>();
        static List<KeyframeSample> __samples = new List<KeyframeSample>();

        static float Round0( float f ) {
            return Math.Abs( f ) < TangentEpsilon ? 0 : f;
        }

        static List<KeyframeSample> TakeSamples( AnimationClipCurveData curveData, out float maxValue, out float minValue, out float averageValue ) {
            List<KeyframeSample> samples = __samples;
            samples.Clear();
            var valueSum = 0.0f;
            var min = float.MaxValue;
            var max = float.MinValue;
            var curve = curveData.curve;
            var keys = curve.keys;
            var kcount = keys.Length;
            for ( int k = 0; k < kcount; ++k ) {
                var v = keys[ k ].value;
                if ( v > max ) {
                    max = v;
                }
                if ( v < min ) {
                    min = v;
                }
                valueSum += v;
                var sample = new KeyframeSample();
                sample.keyframe = keys[ k ];
                sample.pos = new Vector2( keys[ k ].time, v );
                samples.Add( sample );
            }
            maxValue = max;
            minValue = min;
            averageValue = valueSum / kcount;
            return samples;
        }

        static List<Keyframe> CopyKeyframes( AnimationClipCurveData curveData ) {
            var curve = curveData.curve;
            return curve.keys.ToList();
        }

        static void TrimKeyframes( ref List<Keyframe> keyframes, List<KeyframeSample> samples, float epsilon ) {
            var kcount = samples.Count;
            var lastIsRemoved = false;
            var lastKeyframe = new Keyframe();
            var error = 0.0f;
            var lastOut = Round0( samples[ 0 ].keyframe.outTangent );
            keyframes.Add( samples[ 0 ].keyframe );
            for ( var k = 1; k < kcount - 1; ++k ) {
                var kf = samples[ k ].keyframe;
                var diff = samples[ k ].pos.y - samples[ k - 1 ].pos.y;
                var _in = ( samples[ k ].pos.y - samples[ k - 1 ].pos.y ) / ( samples[ k ].pos.x - samples[ k - 1 ].pos.x );
                var _out = ( samples[ k + 1 ].pos.y - samples[ k ].pos.y ) / ( samples[ k + 1 ].pos.x - samples[ k ].pos.x );
                var curIn = Round0( kf.inTangent );
                _in = Round0( _in );
                _out = Round0( _out );
                error += diff;
                var lastkf = keyframes[ keyframes.Count - 1 ];
                var skip = false;
                var nextLinearValue = lastkf.value + ( kf.time - lastkf.time ) * lastkf.outTangent;
                if ( Math.Abs( kf.inTangent - lastkf.outTangent ) < TangentEpsilon &&
                    Math.Abs( kf.inTangent - kf.outTangent ) < TangentEpsilon ) {
                    if ( Mathf.Abs( nextLinearValue - kf.value ) < epsilon ) {
                        skip = true;
                    }
                }
                if ( !skip && ( _in * _out < 0 || curIn * lastOut < 0 || Mathf.Abs( error ) > epsilon || Mathf.Abs( curIn - lastOut ) > TangentEpsilon ) ) {
                    if ( lastIsRemoved ) {
                        keyframes.Add( lastKeyframe );
                        lastIsRemoved = false;
                    }
                    keyframes.Add( kf );
                    error = 0;
                    lastOut = Round0( kf.outTangent );
                } else {
                    lastIsRemoved = true;
                    lastKeyframe = kf;
                }
            }
            keyframes.Add( samples[ kcount - 1 ].keyframe );
        }

        static List<Keyframe> TrimPositionKeyframes( AnimationClipCurveData curveData ) {
            var keyframes = __keyframes;
            float maxValue, minValue, averageValue;
            keyframes.Clear();
            var samples = TakeSamples( curveData, out maxValue, out minValue, out averageValue );
            if ( samples.Count <= 2 ) {
                for ( var i = 0; i < samples.Count; ++i ) {
                    keyframes.Add( samples[ i ].keyframe );
                }
                return keyframes;
            }
            var depth = curveData.propertyName.Split( '/' ).Length;
            var epsilon = Mathf.Abs( m_positionError ) * m_depthScale * depth;
            TrimKeyframes( ref keyframes, samples, epsilon );
            return keyframes;
        }

        static List<Keyframe> TrimRotationKeyframes( AnimationClipCurveData curveData ) {
            List<Keyframe> keyframes = __keyframes;
            float maxValue, minValue, averageValue;
            keyframes.Clear();
            List<KeyframeSample> samples = TakeSamples( curveData, out maxValue, out minValue, out averageValue );
            if ( samples.Count <= 2 ) {
                for ( int i = 0; i < samples.Count; ++i ) {
                    keyframes.Add( samples[ i ].keyframe );
                }
                return keyframes;
            }
            int depth = curveData.propertyName.Split( '/' ).Length;
            var epsilon = Mathf.Abs( m_rotationError ) * depth * m_depthScale;
            TrimKeyframes( ref keyframes, samples, epsilon );
            return keyframes;
        }

        static List<Keyframe> TrimScaleKeyframes( AnimationClipCurveData curveData ) {
            var keyframes = __keyframes;
            float maxValue, minValue, averageValue;
            keyframes.Clear();
            var samples = TakeSamples( curveData, out maxValue, out minValue, out averageValue );
            if ( samples.Count <= 2 ) {
                for ( int i = 0; i < samples.Count; ++i ) {
                    keyframes.Add( samples[ i ].keyframe );
                }
                return keyframes;
            }
            var depth = curveData.propertyName.Split( '/' ).Length;
            var epsilon = Mathf.Abs( m_scaleError ) * depth * m_depthScale;
            TrimKeyframes( ref keyframes, samples, epsilon );
            return keyframes;
        }

        static AnimationClipCurveData TrimClipData( AnimationClipCurveData curveData ) {
            var keyframes = __keyframes;
            var samples = __samples;
            keyframes.Clear();
            samples.Clear();
            if ( curveData.type != typeof( Transform ) ) {
                return curveData;
            }
            var pname = curveData.propertyName;
            if ( pname.IndexOf( "Scale" ) != -1 && m_scaleError > 0 ) {
                keyframes = TrimScaleKeyframes( curveData );
            } else if ( pname.IndexOf( "Position" ) != -1 && m_positionError > 0 ) {
                keyframes = TrimPositionKeyframes( curveData );
            } else if ( !m_removeScaleCurve && pname.IndexOf( "Rotation" ) != -1 && m_rotationError > 0 ) {
                keyframes = TrimRotationKeyframes( curveData );
            } else {
                keyframes = CopyKeyframes( curveData );
            }
            var oriKeys = curveData.curve.keys;
            var kcount = oriKeys.Length;
            if ( keyframes.Count != kcount ) {
                var newData = new AnimationClipCurveData();
                newData.path = curveData.path;
                newData.propertyName = curveData.propertyName;
                newData.type = curveData.type;
                var oldCurve = curveData.curve;
                var newCurve = new AnimationCurve();
                newCurve.postWrapMode = oldCurve.postWrapMode;
                newCurve.preWrapMode = oldCurve.preWrapMode;
                var kfs = keyframes.ToArray();
                newCurve.keys = kfs;
                newData.curve = newCurve;
                curveData = newData;
            }
            return curveData;
        }

        public static bool TrimAnimationClip( ref AnimationClip clip, ref AnimationClip oriClip ) {
            var path = AssetDatabase.GetAssetPath( clip );
            if ( String.IsNullOrEmpty( path ) == false && oriClip != null ) {
                var clipName = oriClip.name;
                var curves = AnimationUtility.GetAllCurves( oriClip, true );
                if ( curves == null || curves.Length == 0 ) {
                    return false;
                }
                var animationClip = UnityEngine.Object.Instantiate( clip ) as AnimationClip;
                animationClip.ClearCurves();
                for ( int c = 0; c < curves.Length; ++c ) {
                    var curveData = TrimClipData( curves[ c ] );
                    if ( m_removeScaleCurve == false || curveData.propertyName.IndexOf( "Scale" ) == -1 ) {
                        animationClip.SetCurve( curveData.path, curveData.type, curveData.propertyName, curveData.curve );
                    }
                }
                String _path = String.Empty;
                String _ext = String.Empty;
                StringUtils.SplitBaseFilename( path, out _path, out _ext );
                var tempFile = AnimationClipCompressImp.OutputRoot + "/" + Path.GetFileName( path );
                var tempFileMeta = tempFile + ".meta";
                if ( File.Exists( tempFile ) ) {
                    AssetDatabase.DeleteAsset( tempFile );
                    AssetDatabase.Refresh();
                    int c = 0;
                    if ( File.Exists( tempFile ) ) {
                        File.Delete( tempFile );
                        ++c;
                    }
                    if ( File.Exists( tempFileMeta ) ) {
                        File.Delete( tempFileMeta );
                        ++c;
                    }
                    if ( c > 0 ) {
                        AssetDatabase.Refresh();
                    }
                }
                Resources.UnloadAsset( clip );
                AssetDatabase.CreateAsset( animationClip, tempFile );
                animationClip.name = clipName;
                AssetDatabase.Refresh();
                if ( File.Exists( tempFile ) &&
                    File.Exists( tempFileMeta ) ) {
                    if ( clip != null ) {
                        Resources.UnloadAsset( clip );
                        clip = null;
                    }
                    // deleting a file seems like an asynchronous operation,
                    // so rename it first to avoid conflict
                    var deleteFile = path + ".deleted";
                    File.Move( path, deleteFile );
                    File.Delete( deleteFile );
                    if ( File.Exists( path ) ) {
                        AssetDatabase.DeleteAsset( tempFile );
                        return false;
                    }
                    try {
                        File.Move( tempFile, path );
                    } catch ( Exception e ) {
                        ULogFile.sharedInstance.LogError( "Rename {0} -> {1}, {2}", tempFile, path, e );
                        AssetDatabase.DeleteAsset( tempFile );
                        return false;
                    }
                    File.Delete( tempFileMeta );
                    var o = AssetDatabase.LoadAssetAtPath( path, typeof( AnimationClip ) ) as AnimationClip;
                    if ( o != null ) {
                        o.name = clipName;
                        EditorUtility.SetDirty( o );
                        clip = o;
                    }
                    AssetDatabase.Refresh();
                    return true;
                }
            }
            return false;
        }
    }

    internal class AnimationClipCompressImp {

        internal const String InputRoot = "Assets";
        internal const String OutputRoot = "Assets/__backup_animations__";

        const String RecordRoot = "Assets/__export_record__";
        const String RecordFileName = "AnimationCompressRecord.json";
        static Dictionary<String, String> m_records = null;

        static String GetRecordFilePath() {
            return RecordRoot + "/" + RecordFileName;
        }

        static String GetTempRecordFilePath() {
            return RecordRoot + "/" + RecordFileName + ".tmp";
        }

        static bool ReadRecordFromFile() {
            if ( m_records == null ) {
                m_records = new Dictionary<String, String>();
            } else {
                m_records.Clear();
            }
            var _InputRoot = "^" + InputRoot;
            var fullPath = GetRecordFilePath();
            if ( File.Exists( fullPath ) ) {
                try {
                    var text = File.ReadAllText( fullPath );
                    var obj = JSONObject.Create( text );
                    if ( obj != null ) {
                        var keys = obj.keys;
                        if ( keys != null ) {
                            for ( int i = 0; i < keys.Count; ++i ) {
                                var path = keys[ i ];
                                var info = obj.GetField( path );
                                if ( info != null && info.type == JSONObject.Type.STRING ) {
                                    if ( !System.IO.File.Exists( path ) ) {
                                        continue;
                                    }
                                    if ( !path.StartsWith( OutputRoot ) ) {
                                        EditorUtility.DisplayProgressBar(
                                            "Load compressed animation record" + new String( '.', Common.Utils.GetSystemTicksSec() % 3 + 1 ),
                                            path, ( float )i / keys.Count );
                                        var dstFilePath = Regex.Replace( path, _InputRoot, OutputRoot );
                                        if ( !System.IO.File.Exists( dstFilePath ) ) {
                                            continue;
                                        }
                                        m_records[ path ] = info.str;
                                    }
                                }
                            }
                        }
                        return true;
                    }
                } catch ( Exception e ) {
                    ULogFile.sharedInstance.LogException( e );
                }
            }
            return false;
        }

        static void AddRecord( String path, String md5 ) {
            if ( m_records == null ) {
                ReadRecordFromFile();
            }
            m_records[ path ] = md5;
        }

        static bool DoesRecordExist( String path, String md5 ) {
            if ( m_records == null ) {
                ReadRecordFromFile();
            }
            String existMD5 = null;
            return m_records.TryGetValue( path, out existMD5 ) && String.Equals( existMD5, md5 );
        }

        static String GetRecord( String path ) {
            if ( m_records == null ) {
                ReadRecordFromFile();
            }
            String existMD5 = null;
            m_records.TryGetValue( path, out existMD5 );
            return existMD5 ?? String.Empty;
        }

        static bool SaveRecordFile( bool temp = false ) {
            if ( m_records == null ) {
                return false;
            }
            var path = temp ? GetTempRecordFilePath() : GetRecordFilePath();
            FileUtils.CreateDirectory( path );
            var keys = m_records.Keys.ToList();
            keys.Sort();
            var root = new JSONObject();
            for ( int i = 0; i < keys.Count; ++i ) {
                root.AddField( keys[ i ], m_records[ keys[ i ] ] );
            }
            File.WriteAllText( path, root.Print( true ) );
            return true;
        }

        static void RemoveTempRecordFile() {
            try {
                var path = GetTempRecordFilePath();
                if ( File.Exists( path ) ) {
                    File.Delete( path );
                }
                path = GetTempRecordFilePath() + ".meta";
                if ( File.Exists( path ) ) {
                    File.Delete( path );
                }
            } catch ( Exception e ) {
                ULogFile.sharedInstance.LogException( e );
            }
        }

        static bool ClearRecord( String path ) {
            if ( m_records == null ) {
                ReadRecordFromFile();
            }
            return m_records.Remove( path );
        }

        static int ClearAllWildAssets() {
            var count = 0;
            var path = OutputRoot;
            if ( Directory.Exists( path ) ) {
                var files = FileUtils.GetFileList( path, EditorUtils.FileFilter_prefab );
                for ( int i = 0; i < files.Count; ++i ) {
                    var dstPath = files[ i ];
                    var srcPath = dstPath.Replace( OutputRoot, InputRoot );
                    if ( !File.Exists( srcPath ) ) {
                        ULogFile.sharedInstance.Log( "Delete wild file: {0}", dstPath );
                        AssetDatabase.DeleteAsset( dstPath );
                        ClearRecord( srcPath );
                        ++count;
                    }
                }
            }
            return count;
        }

        static String DumpAnimationClip( AnimationClip clip ) {
            var sb = StringUtils.newStringBuilder;
            sb.AppendLine( clip.name );
            var curveData = AnimationUtility.GetAllCurves( clip, true );
            for ( int i = 0; i < curveData.Length; ++i ) {
                var curve = curveData[ i ].curve;
                var keys = curve.keys;
                sb.AppendLine( curveData[ i ].type.ToString() );
                sb.AppendLine( curveData[ i ].propertyName );
                sb.AppendLine( curveData[ i ].path );
                sb.AppendLine( curve.preWrapMode.ToString() );
                sb.AppendLine( curve.postWrapMode.ToString() );
                sb.AppendLine( keys.Length.ToString() );
                for ( int j = 0; j < keys.Length; ++j ) {
                    sb.AppendLine( keys[ j ].time.ToString() );
                    sb.AppendLine( keys[ j ].value.ToString() );
                    sb.AppendLine( keys[ j ].inTangent.ToString() );
                    sb.AppendLine( keys[ j ].outTangent.ToString() );
                    sb.AppendLine( keys[ j ].tangentMode.ToString() );
                }
            }
            return sb.ToString();
        }

        static String HashAnimationClip( String path ) {
            var clip = AssetDatabase.LoadAssetAtPath( path, typeof( AnimationClip ) ) as AnimationClip;
            if ( clip == null ) {
                return String.Empty;
            }
            try {
                var str = DumpAnimationClip( clip );
                return !String.IsNullOrEmpty( str ) ? EditorUtils.Md5String( str ) : String.Empty;
            } finally {
                Resources.UnloadAsset( clip );
            }
        }

        static void DumpAnimationClipGUI() {
            var clip = Selection.activeObject as AnimationClip;
            if ( clip != null ) {
                var str = DumpAnimationClip( clip );
                UDebug.Print( str );
            }
        }

        public static void ExportAll(
            List<AnimationClipCompressor.ClipInfo> input, bool saveOnly = false ) {
            int count = 0;
            int count1 = 0;
            try {
                ReadRecordFromFile();
                count1 = ClearAllWildAssets();
                RemoveTempRecordFile();
                var _InputRoot = "^" + InputRoot;
                var batchSaveRecords = new List<Func<String>>();
                var defaultArg = new AnimationClipCompressor.Arg();
                input = input.Where( _c => _c.selected ).ToList();
                for ( int i = 0; i < input.Count; ++i ) {
                    var name = input[ i ].clip.name;
                    var arg = input[ i ]._arg ?? defaultArg;
                    var srcFilePath = input[ i ].path;
                    if ( srcFilePath.Contains( OutputRoot ) ) {
                        continue;
                    }
                    var dstFilePath = Regex.Replace( srcFilePath, _InputRoot, OutputRoot );
                    UDebug.Print( "{0} => {1}", srcFilePath, dstFilePath );
                    if ( EditorUtility.DisplayCancelableProgressBar(
                        "Backup AnimationClips" + new String( '.', Common.Utils.GetSystemTicksSec() % 3 + 1 ), srcFilePath, ( float )i / input.Count ) ) {
                        break;
                    }
                    var srcHash = HashAnimationClip( srcFilePath );
                    var dstHash = HashAnimationClip( dstFilePath );
                    var fullHash = srcHash + " | " + dstHash + " : " + arg.ToString();
                    var oldHash = GetRecord( srcFilePath );
                    if ( oldHash != fullHash ) {
                        // src animation curve changed, re-backup first
                        if ( saveOnly == false && !oldHash.StartsWith( srcHash ) ) {
                            String outBasename;
                            String outExtention;
                            String outPath;
                            StringUtils.SplitFullFilename( dstFilePath, out outBasename, out outExtention, out outPath );
                            outPath = outPath.Trim( '/' );
                            if ( !Directory.Exists( outPath ) ) {
                                FileUtils.CreateDirectory( outPath );
                                AssetDatabase.Refresh();
                            }
                            var dstAsset = AssetDatabase.LoadAssetAtPath( dstFilePath, typeof( AnimationClip ) ) as AnimationClip;
                            if ( dstAsset != null ) {
                                Resources.UnloadAsset( dstAsset );
                                AssetDatabase.DeleteAsset( dstFilePath );
                                dstAsset = null;
                                AssetDatabase.Refresh();
                            }
                            if ( !AssetDatabase.CopyAsset( srcFilePath, dstFilePath ) ) {
                                ULogFile.sharedInstance.LogError( "Copy Asset Failed: {0} -> {1}", srcFilePath, dstFilePath );
                                continue;
                            }
                            AssetDatabase.Refresh();
                        }
                        try {
                            if ( saveOnly == false ) {
                                CurveCompressor.m_positionError = arg.positionError;
                                CurveCompressor.m_rotationError = arg.rotationError;
                                CurveCompressor.m_scaleError = arg.scaleError;
                                CurveCompressor.m_depthScale = arg.depthScale;
                                CurveCompressor.m_removeScaleCurve = arg.removeScaleCurve;
                                var srcClip = AssetDatabase.LoadAssetAtPath( srcFilePath, typeof( AnimationClip ) ) as AnimationClip;
                                var oriClip = AssetDatabase.LoadAssetAtPath( dstFilePath, typeof( AnimationClip ) ) as AnimationClip;
                                if ( srcClip != null && oriClip != null ) {
                                    var b = CurveCompressor.TrimAnimationClip( ref srcClip, ref oriClip );
                                    if ( !b ) {
                                        ULogFile.sharedInstance.LogError( "Compress Animation Failed: {0}", srcFilePath );
                                        continue;
                                    }
                                    if ( srcClip != null ) {
                                        Resources.UnloadAsset( srcClip );
                                    }
                                    var fi = new FileInfo( srcFilePath );
                                    if ( fi.Exists ) {
                                        input[ i ].compressed_size = ( int )fi.Length;
                                    }
                                } else {
                                    continue;
                                }
                            }
                            var _srcFilePath = String.Copy( srcFilePath );
                            var _dstFilePath = String.Copy( dstFilePath );
                            batchSaveRecords.Add(
                                () => {
                                    var _srcHash = HashAnimationClip( _srcFilePath );
                                    var _dstHash = HashAnimationClip( _dstFilePath );
                                    var _fullHash = _srcHash + " | " + _dstHash + " : " + arg.ToString();
                                    AddRecord( _srcFilePath, _fullHash );
                                    return _srcFilePath;
                                }
                            );
                        } catch ( Exception e ) {
                            ULogFile.sharedInstance.LogException( e );
                        }
                    }
                }
                if ( batchSaveRecords.Count > 0 ) {
                    AssetDatabase.Refresh();
                    AssetDatabase.SaveAssets();
                    for ( int i = 0; i < batchSaveRecords.Count; ++i ) {
                        try {
                            var srcFile = batchSaveRecords[ i ]();
                            EditorUtility.DisplayProgressBar( "Save" + new String( '.', Common.Utils.GetSystemTicksSec() % 3 + 1 ), srcFile, ( float )i / batchSaveRecords.Count );
                        } catch ( Exception e ) {
                            ULogFile.sharedInstance.LogException( e );
                        }
                    }
                }
                count = batchSaveRecords.Count;
            } finally {
                SaveRecordFile();
                RemoveTempRecordFile();
                EditorUtility.ClearProgressBar();
                if ( count > 0 || count1 > 0 ) {
                    EditorUtils.BrowseFolder( RecordRoot );
                }
            }
        }
    }

    internal class AnimationClipCompressorArgEditor : EditorWindow {

        class ClipInfo {
            internal AnimationClipCompressor.Arg arg;
            internal AnimationClip clip;
            internal AssetImporter importer;
            internal String args = String.Empty;

            internal void Apply() {
                if ( importer != null ) {
                    var s = importer.userData;
                    if ( arg != null ) {
                        var root = new JSONObject( JSONObject.Type.OBJECT );
                        root.SetField( "p", arg.positionError );
                        root.SetField( "r", arg.rotationError );
                        root.SetField( "s", arg.scaleError );
                        root.SetField( "d", arg.depthScale );
                        root.SetField( "rms", arg.removeScaleCurve );
                        importer.userData = root.ToString( true );
                    } else {
                        importer.userData = String.Empty;
                    }
                    if ( importer.userData != s ) {
                        importer.SaveAndReimport();
                    }
                }
            }
            internal void Reset() {
                arg = null;
                args = String.Empty;
                if ( importer != null ) {
                    var s = importer.userData;
                    if ( !String.IsNullOrEmpty( s ) ) {
                        importer.userData = String.Empty;
                        importer.SaveAndReimport();
                    }
                }
            }
        }

        static AnimationClipCompressorArgEditor m_window = null;
        static List<ClipInfo> m_selections = null;
        static AnimationClipCompressor.Arg m_argEditing = new AnimationClipCompressor.Arg();
        static Vector2 m_scrollViewPos = Vector2.zero;

        static void Init() {
            if ( m_window == null ) {
                m_window = ( AnimationClipCompressorArgEditor )EditorWindow.GetWindow( typeof( AnimationClipCompressorArgEditor ), false, "AnimationClip Compressor Arg Editor" );
                Selection.selectionChanged += OnSelectionChanged;
            }
        }

        void OnDestroy() {
            m_window = null;
            Selection.selectionChanged -= OnSelectionChanged;
        }

        static void OnSelectionChanged() {
            if ( m_window != null ) {
                var objs = Selection.objects;
                m_selections = m_selections ?? new List<ClipInfo>();
                m_selections.Clear();
                for ( int i = 0; i < objs.Length; ++i ) {
                    if ( objs[ i ].GetType() == typeof( AnimationClip ) ) {
                        var path = AssetDatabase.GetAssetPath( objs[ i ] );
                        if ( !String.IsNullOrEmpty( path ) ) {
                            var clip = objs[ i ] as AnimationClip;
                            if ( clip != null ) {
                                var clipInfo = new ClipInfo();
                                clipInfo.clip = clip;
                                clipInfo.arg = null;
                                clipInfo.args = String.Empty;
                                var importer = AssetImporter.GetAtPath( path );
                                if ( importer != null ) {
                                    clipInfo.importer = importer;
                                    if ( !String.IsNullOrEmpty( importer.userData ) ) {
                                        try {
                                            var root = JSONObject.Create( importer.userData );
                                            clipInfo.arg = new AnimationClipCompressor.Arg();
                                            root.GetField( out clipInfo.arg.positionError, "p", clipInfo.arg.positionError );
                                            root.GetField( out clipInfo.arg.rotationError, "r", clipInfo.arg.rotationError );
                                            root.GetField( out clipInfo.arg.scaleError, "s", clipInfo.arg.scaleError );
                                            root.GetField( out clipInfo.arg.depthScale, "d", clipInfo.arg.depthScale );
                                            root.GetField( out clipInfo.arg.removeScaleCurve, "rms", clipInfo.arg.removeScaleCurve );
                                        } catch ( Exception e ) {
                                            UDebug.LogException( e );
                                        }
                                    }
                                    if ( clipInfo.arg != null ) {
                                        clipInfo.args = clipInfo.arg.ToString();
                                    }
                                    m_selections.Add( clipInfo );
                                }
                            }
                        }
                    }
                }
                if ( m_selections.Count > 0 ) {
                    m_argEditing = new AnimationClipCompressor.Arg();
                    for ( int i = 0; i < m_selections.Count; ++i ) {
                        var info = m_selections[ i ];
                        if ( info.arg != null ) {
                            m_argEditing.positionError = info.arg.positionError;
                            m_argEditing.rotationError = info.arg.rotationError;
                            m_argEditing.scaleError = info.arg.scaleError;
                            m_argEditing.depthScale = info.arg.depthScale;
                            m_argEditing.removeScaleCurve = info.arg.removeScaleCurve;
                            break;
                        }
                    }
                }
                m_window.Repaint();
            } else {
                Selection.selectionChanged -= OnSelectionChanged;
            }
        }

        class GUIChangeColor : IDisposable {
            public GUIChangeColor( Color newColor ) {
                this.PreviousColor = GUI.color;
                GUI.color = newColor;
            }
            public void Dispose() {
                GUI.color = this.PreviousColor;
            }
            [SerializeField]
            private Color PreviousColor { get; set; }
        }

        void OnGUI() {
            EditorGUILayout.BeginVertical();
            if ( m_selections != null && m_selections.Count > 0 ) {
                var color = GUI.color;
                var args = String.Empty;
                EditorGUILayout.BeginVertical();
                {
                    GUI.color = m_argEditing.positionError > 0 ? Color.white : Color.gray;
                    m_argEditing.positionError = EditorGUILayout.FloatField( "Position Error:", m_argEditing.positionError );
                    m_argEditing.positionError = Mathf.Clamp( m_argEditing.positionError, 0, 1 );
                    GUI.color = m_argEditing.rotationError > 0 ? Color.white : Color.gray;
                    m_argEditing.rotationError = EditorGUILayout.FloatField( "Rotation Error:", m_argEditing.rotationError );
                    m_argEditing.rotationError = Mathf.Clamp( m_argEditing.rotationError, 0, 1 );
                    GUI.color = m_argEditing.scaleError > 0 ? Color.white : Color.gray;
                    m_argEditing.scaleError = EditorGUILayout.FloatField( "Scale Error:", m_argEditing.scaleError );
                    m_argEditing.scaleError = Mathf.Clamp( m_argEditing.scaleError, 0, 1 );
                    GUI.color = color;
                    m_argEditing.depthScale = EditorGUILayout.FloatField( "Depth Scale:", m_argEditing.depthScale );
                    m_argEditing.depthScale = Math.Max( m_argEditing.depthScale, 1 );
                    m_argEditing.depthScale = Mathf.Clamp( m_argEditing.depthScale, 1, 2 );
                    m_argEditing.removeScaleCurve = EditorGUILayout.Toggle( "Remove Scale Curve", m_argEditing.removeScaleCurve );
                    args = m_argEditing.ToString();
                    if ( GUILayout.Button( "Apply All" ) ) {
                        for ( int i = 0; i < m_selections.Count; ++i ) {
                            m_selections[ i ].arg = m_argEditing.Clone();
                            m_selections[ i ].args = args;
                            m_selections[ i ].Apply();
                        }
                    }
                    if ( GUILayout.Button( "Reset All" ) ) {
                        for ( int i = 0; i < m_selections.Count; ++i ) {
                            m_selections[ i ].arg = null;
                            m_selections[ i ].args = String.Empty;
                            m_selections[ i ].Reset();
                        }
                    }
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
                EditorGUILayout.BeginVertical();
                m_scrollViewPos = EditorGUILayout.BeginScrollView( m_scrollViewPos );
                for ( int i = 0; i < m_selections.Count; ++i ) {
                    EditorGUILayout.BeginHorizontal();
                    var clipInfo = m_selections[ i ];
                    using ( new GUIChangeColor( clipInfo.args == args ? Color.white : Color.red ) ) {
                        EditorGUILayout.ObjectField( clipInfo.clip.name, clipInfo.clip, typeof( AnimationClip ), false );
                    }
                    if ( GUILayout.Button( "Apply", GUILayout.Width( 60 ) ) ) {
                        clipInfo.arg = m_argEditing.Clone();
                        clipInfo.args = m_argEditing.ToString();
                        clipInfo.Apply();
                    }
                    if ( GUILayout.Button( "Reset", GUILayout.Width( 60 ) ) ) {
                        clipInfo.Reset();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();
        }

    }
}
