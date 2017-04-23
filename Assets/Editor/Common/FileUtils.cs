using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Common {

    public static class FileUtils {

        public static FileStream OpenFile( String fullPath ) {
            FileStream fs = null;
            try {
                fs = File.Open( fullPath, FileMode.Open, FileAccess.Read, FileShare.Read );
            } catch ( Exception e ) {
                Common.ULogFile.sharedInstance.LogException( e );
            }
            return fs;
        }

        // create a directory
        // each sub directories will be created if any of them don't exist.
        public static bool CreateDirectory( String dirName ) {
            try {
                // first remove file name and extension;
                var ext = Path.GetExtension( dirName );
                String fileNameAndExt = Path.GetFileName( dirName );
                if ( !String.IsNullOrEmpty( fileNameAndExt ) && !string.IsNullOrEmpty( ext ) ) {
                    dirName = dirName.Substring( 0, dirName.Length - fileNameAndExt.Length );
                }
                var sb = new StringBuilder();
                var dirs = dirName.Split( '/', '\\' );
                if ( dirs.Length > 0 ) {
                    if ( dirName[ 0 ] == '/' ) {
                        // abs path tag on Linux OS
                        dirs[ 0 ] = "/" + dirs[ 0 ];
                    }
                }
                for ( int i = 0; i < dirs.Length; ++i ) {
                    if ( dirs[ i ].Length == 0 ) {
                        continue;
                    }
                    if ( sb.Length != 0 ) {
                        sb.Append( '/' );
                    }
                    sb.Append( dirs[ i ] );
                    var cur = sb.ToString();
                    if ( String.IsNullOrEmpty( cur ) ) {
                        continue;
                    }
                    if ( !Directory.Exists( cur ) ) {
                        var info = Directory.CreateDirectory( cur );
                        if ( null == info ) {
                            return false;
                        }
                    }
                }
                return true;
            } catch ( Exception e ) {
                ULogFile.sharedInstance.LogException( e );
            }
            return false;
        }

        public static bool CopyDirectory( String sourceDirName, String destDirName, bool copySubDirs ) {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo( sourceDirName );
            if ( !dir.Exists ) {
                return false;
            }
            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if ( !Directory.Exists( destDirName ) ) {
                Directory.CreateDirectory( destDirName );
            }
            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach ( FileInfo file in files ) {
                string temppath = Path.Combine( destDirName, file.Name );
                file.CopyTo( temppath, false );
            }
            var ret = true;
            // If copying subdirectories, copy them and their contents to new location.
            if ( copySubDirs ) {
                foreach ( DirectoryInfo subdir in dirs ) {
                    string temppath = Path.Combine( destDirName, subdir.Name );
                    ret &= CopyDirectory( subdir.FullName, temppath, copySubDirs );
                }
            }
            return ret;
        }

        public enum TreeWalkerCmd {
            Continue,
            Skip,
            Exit,
        }

        public interface ITreeWalker {
            bool IsRecursive();
            // will be called for each file while WalkTree is running
            TreeWalkerCmd DoFile( String name );
            // will be called for each directory while WalkTree is running
            TreeWalkerCmd DoDirectory( String name );
            // wildmatch pattern
            String FileSearchPattern();
            String DirectorySearchPattern();
        }

        public class BaseTreeWalker : ITreeWalker {
            public virtual bool IsRecursive() { return true; }
            public virtual TreeWalkerCmd DoFile( String name ) { return TreeWalkerCmd.Continue; }
            public virtual TreeWalkerCmd DoDirectory( String name ) { return TreeWalkerCmd.Continue; }
            public virtual String FileSearchPattern() { return "*"; }
            public virtual String DirectorySearchPattern() { return "*"; }
        }

        class TreeDeleter : BaseTreeWalker, IDisposable {
            public List<String> fileList = new List<String>();
            public List<String> dirList = new List<String>();
            public override bool IsRecursive() { return true; }
            public override TreeWalkerCmd DoFile( String name ) {
                fileList.Add( name );
                return TreeWalkerCmd.Continue;
            }
            public override TreeWalkerCmd DoDirectory( String name ) {
                dirList.Add( name );
                return TreeWalkerCmd.Continue;
            }
            public void Dispose() {
                try {
                    for ( int i = 0; i < fileList.Count; ++i ) {
                        File.Delete( fileList[i] );
                    }
                    for ( int i = dirList.Count - 1; i >= 0; --i ) {
                        Directory.Delete( dirList[i] );
                    }
                } catch ( Exception e ) {
                    UDebug.LogException( e );
                }
            }
        }

        public static bool RemoveTree( String dirName, bool delSelf = false ) {
            int count = 0;
            using ( var td = new TreeDeleter() ) {
                WalkTree( dirName, td );
                count = td.dirList.Count + td.fileList.Count;
            }
            if ( delSelf ) {
                try {
                    if ( Directory.Exists( dirName ) ) {
                        Directory.Delete( dirName );
                    }
                } catch ( Exception e ) {
                    UDebug.LogException( e );
                }
            }
            return count != 0;
        }

        public static void WalkTree( String dirName, ITreeWalker walker ) {
            var dirCount = 0;
            dirName = Common.StringUtils.StandardisePath( dirName );
            Stack<String> dirStack = new Stack<String>();
            dirStack.Push( dirName );
            while ( dirStack.Count > 0 ) {
                String lastPath = dirStack.Pop();
                DirectoryInfo di = new DirectoryInfo( lastPath );
                if ( !di.Exists || ( ( di.Attributes & FileAttributes.Hidden ) != 0 && dirCount > 0 ) ) {
                    continue;
                }
                ++dirCount;
                foreach ( FileInfo fileInfo in di.GetFiles( walker.FileSearchPattern() ) ) {
                    // compose full file name from dirName
                    String f = lastPath;
                    if ( f[f.Length - 1] == '/' ) {
                        f += fileInfo.Name;
                    } else {
                        f = f + "/" + fileInfo.Name;
                    }
                    var cmd = walker.DoFile( f );
                    switch ( cmd ) {
                    case TreeWalkerCmd.Skip:
                        continue;
                    case TreeWalkerCmd.Exit:
                        goto EXIT;
                    }
                }
                if ( walker.IsRecursive() ) {
                    foreach ( DirectoryInfo dirInfo in di.GetDirectories( walker.DirectorySearchPattern() ) ) {
                        // compose full path name from dirName
                        String p = lastPath;
                        if ( p[p.Length - 1] == '/' ) {
                            p += dirInfo.Name;
                        } else {
                            p = p + "/" + dirInfo.Name;
                        }
                        FileAttributes fa = File.GetAttributes( p );
                        if ( ( fa & FileAttributes.Hidden ) == 0 ) {
                            var cmd = walker.DoDirectory( p );
                            switch ( cmd ) {
                            case TreeWalkerCmd.Skip:
                                continue;
                            case TreeWalkerCmd.Exit:
                                goto EXIT;
                            }
                            dirStack.Push( p );
                        }
                    }
                }
            }
        EXIT:
            ;
        }

        class DirectoryScanner : BaseTreeWalker {
            List<String> m_allDirs;
            Func<String, Boolean> m_filter;
            bool m_recursive;
            public DirectoryScanner( List<String> fs, Func<String, Boolean> filter, bool recursive ) {
                m_allDirs = fs;
                m_filter = filter;
                m_recursive = recursive;
            }
            public override bool IsRecursive() {
                return m_recursive;
            }
            public override TreeWalkerCmd DoDirectory( String name ) {
                if ( m_filter == null || !m_filter( name ) ) {
                    m_allDirs.Add( name );
                }
                return TreeWalkerCmd.Continue;
            }
        }

        class FileScanner : BaseTreeWalker {
            List<String> m_allFiles;
            Func<String, Boolean> m_filter;
            bool m_recursive;
            public FileScanner( List<String> fs, Func<String, Boolean> filter, bool recursive ) {
                m_allFiles = fs;
                m_filter = filter;
                m_recursive = recursive;
            }
            public override bool IsRecursive() {
                return m_recursive;
            }
            public override TreeWalkerCmd DoFile( String name ) {
                if ( m_filter == null || m_filter( name ) ) {
                    m_allFiles.Add( name );
                }
                return TreeWalkerCmd.Continue;
            }
        }

        public static List<String> GetFileList( String path, Func<String, Boolean> filter, bool recursive = true ) {
            var ret = new List<String>();
            if ( !String.IsNullOrEmpty( path ) ) {
                var fs = new FileScanner( ret, filter, recursive );
                WalkTree( path, fs );
            }
            return ret;
        }

        public static List<String> GetDirectoryList( String path, Func<String, Boolean> filter, bool recursive = true ) {
            var ret = new List<String>();
            if ( !String.IsNullOrEmpty( path ) ) {
                var fs = new DirectoryScanner( ret, filter, recursive );
                WalkTree( path, fs );
            }
            return ret;
        }
    }
}
