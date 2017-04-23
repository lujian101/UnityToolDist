#if UNITY_EDITOR
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Reflection;
using Common;

namespace AresEditor.ArcReactor {

    public static class EditorUtils {

        public static readonly String ProjectDirectory = String.Empty;

        static EditorUtils() {
            ProjectDirectory = EditorUtils.GetProjectUnityRootPath();
        }

        public static String GetProjectRootPath() {
            string rootPath = Application.dataPath + "/../..";
            if ( Directory.Exists( rootPath ) ) {
                rootPath = Path.GetFullPath( rootPath );
                return rootPath.Replace( '\\', '/' );
            } else {
                return rootPath;
            }
        }

        public static String GetProjectUnityRootPath() {
            string rootPath = Application.dataPath + "/..";
            if ( Directory.Exists( rootPath ) ) {
                rootPath = Path.GetFullPath( rootPath );
                return rootPath.Replace( '\\', '/' );
            } else {
                return rootPath;
            }
        }

        public static String GetProjectUnityRootName() {
            var path = GetProjectUnityRootPath().TrimEnd( '/' );
            return path.Substring( path.LastIndexOf( '/' ) + 1 );
        }

        public static void BrowseFolderWin( String path, bool selectFile = false ) {
            if ( Application.platform == RuntimePlatform.WindowsEditor ) {
                System.Diagnostics.Process prcShell = new System.Diagnostics.Process();
                prcShell.StartInfo.FileName = "explorer.exe";
                path = path.Replace( '/', '\\' );
                if ( selectFile && File.Exists( path ) ) {
                    prcShell.StartInfo.Arguments = "/select, " + path;
                } else {
                    prcShell.StartInfo.Arguments = path;
                }
                prcShell.Start();
            } else {
                UnityEngine.Debug.Log( "Log dir: " + path );
            }
        }

        public static void BrowseFolder( String path ) {
            if ( Application.isEditor ) {
                System.Diagnostics.Process prcShell = new System.Diagnostics.Process();
                if ( Application.platform == RuntimePlatform.OSXEditor ) {
                    prcShell.StartInfo.FileName = "open";
                    path = path.Replace( '\\', '/' );
                } else if ( Application.platform == RuntimePlatform.WindowsEditor ) {
                    prcShell.StartInfo.FileName = "explorer.exe";
                    path = path.Replace( '/', '\\' );
                } else {
                    return;
                }
                prcShell.StartInfo.Arguments = path;
                prcShell.Start();
            } else {
                UnityEngine.Debug.Log( "Log dir: " + path );
            }
        }

        public static void ClearLog() {
            var assembly = Assembly.GetAssembly( typeof( SceneView ) );
            var type = assembly.GetType( "UnityEditorInternal.LogEntries" );
            var method = type.GetMethod( "Clear" );
            method.Invoke( null, null );
        }

        public static void CheckCompileError() {
            Assembly assembly = Assembly.GetAssembly( typeof( SceneView ) );
            Type logEntries = assembly.GetType( "UnityEditorInternal.LogEntries" );
            logEntries.GetMethod( "Clear" ).Invoke( null, null );
            int count = ( int )logEntries.GetMethod( "GetCount" ).Invoke( null, null );
            if ( count > 0 ) {
                Common.UDebug.LogError( "Cannot build because you have compile errors!" );
            }
        }

        public static Func<String, bool> FileExtFilter( String ext ) {
            if ( !ext.StartsWith( "." ) ) {
                ext = "." + ext;
            }
            return fileName => fileName.EndsWith( ext, StringComparison.CurrentCultureIgnoreCase );
        }

        public static bool FileFilter_prefab( String fileName ) {
            return fileName.EndsWith( ".prefab", StringComparison.CurrentCultureIgnoreCase );
        }

        public static String RelateToAssetsPath( String path ) {
            if ( path.StartsWith( Application.dataPath ) ) {
                return "Assets" + path.Substring( Application.dataPath.Length );
            }
            return path;
        }

        public static String GetSelectedPath() {
            var path = String.Empty;
            var sel = Selection.GetFiltered( typeof( UnityEngine.Object ), SelectionMode.Assets );
            foreach ( var obj in sel ) {
                path = AssetDatabase.GetAssetPath( obj );
                if ( !string.IsNullOrEmpty( path ) && Directory.Exists( path ) ) {
                    break;
                }
            }
            return path;
        }

        public static List<String> GetAllSelectedPath() {
            var paths = new List<String>();
            var sel = Selection.GetFiltered( typeof( UnityEngine.Object ), SelectionMode.Assets );
            foreach ( var obj in sel ) {
                var path = AssetDatabase.GetAssetPath( obj );
                if ( !string.IsNullOrEmpty( path ) && Directory.Exists( path ) ) {
                    paths.Add( path );
                }
            }
            return paths;
        }

        public static String GetFullPath( String path ) {
            path = Path.GetFullPath( ( new Uri( path ) ).LocalPath );
            path = path.Replace( '\\', '/' );
            if ( FileUtils.CreateDirectory( path ) ) {
                return path;
            } else {
                return String.Empty;
            }
        }

        public static FileStream FileOpenRead( String filePath ) {
            if ( !System.IO.Path.IsPathRooted( filePath ) ) {
                filePath = ProjectDirectory + "/" + filePath;
                filePath = filePath.Replace( System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar );
            }
            return File.OpenRead( filePath );
        }

        static String _Md5Asset( String filePath,
            MD5CryptoServiceProvider md5Service,
            byte[] buffer, StringBuilder sb ) {
            try {
                int bytesRead = 0;
                using ( var file = FileOpenRead( filePath ) ) {
                    while ( ( bytesRead = file.Read( buffer, 0, buffer.Length ) ) > 0 ) {
                        md5Service.TransformBlock( buffer, 0, bytesRead, buffer, 0 );
                    }
                }
                var meta = filePath + ".meta";
                if ( File.Exists( meta ) ) {
                    bytesRead = 0;
                    using ( var file = FileOpenRead( meta ) ) {
                        while ( ( bytesRead = file.Read( buffer, 0, buffer.Length ) ) > 0 ) {
                            md5Service.TransformBlock( buffer, 0, bytesRead, buffer, 0 );
                        }
                    }
                }
                md5Service.TransformFinalBlock( buffer, 0, bytesRead );
                var hashBytes = md5Service.Hash;
                for ( int i = 0; i < hashBytes.Length; i++ ) {
                    sb.Append( hashBytes[ i ].ToString( "x2" ) );
                }
                return sb.ToString();
            } catch ( Exception e ) {
                ULogFile.sharedInstance.LogError( "_Md5Asset: {0}\ncd: {1}", filePath, ProjectDirectory );
                ULogFile.sharedInstance.LogException( e );
            }
            return String.Empty;
        }

        public static String Md5Asset( String filePath ) {
            if ( !File.Exists( filePath ) ) {
                return String.Empty;
            }
            const int chunkSize = 10240;
            var _MD5Service = new MD5CryptoServiceProvider();
            var buffer = new byte[ chunkSize ];
            return _Md5Asset( filePath, _MD5Service, buffer, new StringBuilder() );
        }

        public static String Md5File( String filePath ) {
            try {
                using ( var stream = new BufferedStream( FileOpenRead( filePath ), 4096 ) ) {
                    var _MD5Service = new MD5CryptoServiceProvider();
                    var hashBytes = _MD5Service.ComputeHash( stream );
                    var sb = new StringBuilder();
                    for ( int i = 0; i < hashBytes.Length; i++ ) {
                        sb.Append( hashBytes[ i ].ToString( "x2" ) );
                    }
                    return sb.ToString();
                }
            } catch ( Exception e ) {
                ULogFile.sharedInstance.LogException( e );
            }
            return String.Empty;
        }

        public static String Md5String( String str ) {
            var bytes = UTF8Encoding.Default.GetBytes( str );
            var md5 = new MD5CryptoServiceProvider();
            var hashBytes = md5.ComputeHash( bytes );
            var sb = new StringBuilder();
            for ( int i = 0; i < hashBytes.Length; i++ ) {
                sb.Append( hashBytes[ i ].ToString( "x2" ) );
            }
            return sb.ToString();
        }
}
}
#endif
//EOF
