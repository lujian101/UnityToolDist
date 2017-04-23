using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;

namespace Common {

    public static class StringUtils {

        const int GlobalStringBuilderPoolSize = 10;
        const int MaxGlobalStringBuilderPoolSize = GlobalStringBuilderPoolSize * 4;
        const int StringBuilderInitSize = 128;
        const int StringBuilderMaxSize = 256;

        static Queue<StringBuilder>[] _StringBuilderPoolBuf = null; // allocating & allocated pools
        static int _StringBuilderPoolIndex = 0; // allocator pool index in buf
        
        static StringUtils() {
            _StringBuilderPoolBuf = new Queue<StringBuilder>[ 2 ] {
                new Queue<StringBuilder>(),
                new Queue<StringBuilder>()
            };
            for ( int i = 0; i < GlobalStringBuilderPoolSize; ++i ) {
                _StringBuilderPoolBuf[ _StringBuilderPoolIndex ].Enqueue( new StringBuilder( StringBuilderInitSize ) );
            }
        }

        public static StringBuilder newStringBuilder {
            get {
                var curPool = _StringBuilderPoolBuf[ _StringBuilderPoolIndex ];
                if ( curPool.Count == 0 ) {
                    // swap allocator pool
                    _StringBuilderPoolIndex = ( _StringBuilderPoolIndex + 1 ) & 1;
                    curPool = _StringBuilderPoolBuf[ _StringBuilderPoolIndex ];
                }
                var dstIndex = ( _StringBuilderPoolIndex + 1 ) & 1;
                var dstPool = _StringBuilderPoolBuf[ dstIndex ];
                if ( curPool.Count > 0 ) {
                    var sb = curPool.Dequeue();
                    dstPool.Enqueue( sb );
                    sb.Length = 0;
                    return sb;
                } else {
                    var sb = new StringBuilder( StringBuilderInitSize );
                    dstPool.Enqueue( sb );
                    return sb;
                }
            }
        }

        public static StringBuilder AllocateStringBuilder() {
            var curPool = _StringBuilderPoolBuf[ _StringBuilderPoolIndex ];
            if ( curPool.Count == 0 ) {
                // swap allocator pool
                _StringBuilderPoolIndex = ( _StringBuilderPoolIndex + 1 ) & 1;
                curPool = _StringBuilderPoolBuf[ _StringBuilderPoolIndex ];
            }
            if ( curPool.Count > 0 ) {
                var sb = curPool.Dequeue();
                sb.Length = 0;
                return sb;
            } else {
                return new StringBuilder( StringBuilderInitSize );
            }
        }

        public static void FreeStringBuilder( StringBuilder sb ) {
            var curPool = _StringBuilderPoolBuf[ _StringBuilderPoolIndex ];
            if ( sb != null && curPool.Count < MaxGlobalStringBuilderPoolSize ) {
                sb.Length = 0;
                if ( sb.Capacity > StringBuilderMaxSize ) {
                    sb.Capacity = StringBuilderInitSize;
                }
                curPool.Enqueue( sb );
            }
        }

        public static void SplitFilename( String qualifiedName, out String outBasename, out String outPath ) {
            String path = qualifiedName.Replace( '\\', '/' );
            int i = path.LastIndexOf( '/' );
            if ( i == -1 ) {
                outPath = String.Empty;
                outBasename = qualifiedName;
            } else {
                outBasename = path.Substring( i + 1, path.Length - i - 1 );
                outPath = path.Substring( 0, i + 1 );
            }
        }

        public static String StandardisePath( String init ) {
            String path = init.Replace( '\\', '/' );
            if ( path.Length > 0 && path[ path.Length - 1 ] != '/' ) {
                path += '/';
            }
            return path;
        }

        public static String StandardisePathWithoutSlash( String init ) {
            String path = init.Replace( '\\', '/' );
            while ( path.Length > 0 && path[ path.Length - 1 ] == '/' ) {
                path = path.Remove( path.Length - 1 );
            }
            return path;
        }

        public static String RemoveExtension( String fullName ) {
            int dot = fullName.LastIndexOf( '.' );
            if ( dot != -1 ) {
                return fullName.Substring( 0, dot );
            } else {
                return fullName;
            }
        }

        public static void SplitBaseFilename( String fullName, out String outBasename, out String outExtention ) {
            int i = fullName.LastIndexOf( '.' );
            if ( i == -1 ) {
                outExtention = String.Empty;
                outBasename = fullName;
            } else {
                outExtention = fullName.Substring( i + 1 );
                outBasename = fullName.Substring( 0, i );
            }
        }

        public static String SafeFormat<T>( String format, T arg ) {
            if ( format != null ) {
                try {
                    return String.Format( format, arg );
                } catch ( Exception e ) {
                    UDebug.LogException( e );
                }
            }
            return String.Empty;
        }

        public static String SafeFormat<T1, T2>( String format, T1 arg1, T2 arg2 ) {
            if ( format != null ) {
                try {
                    return String.Format( format, arg1, arg2 );
                } catch ( Exception e ) {
                    UDebug.LogException( e );
                }
            }
            return String.Empty;
        }

        public static String SafeFormat<T1, T2, T3>( String format, T1 arg1, T2 arg2, T3 arg3 ) {
            if ( format != null ) {
                try {
                    return String.Format( format, arg1, arg2, arg3 );
                } catch ( Exception e ) {
                    UDebug.LogException( e );
                }
            }
            return String.Empty;
        }

        public static String SafeFormat( String format, params object[] args ) {
            if ( format != null && args != null ) {
                try {
                    return String.Format( format, args );
                } catch ( Exception e ) {
                    Common.UDebug.LogException( e );
                }
            }
            return String.Empty;
        }

        public static void SplitFullFilename( String qualifiedName, out String outBasename, out String outExtention, out String outPath ) {
            String fullName = String.Empty;
            SplitFilename( qualifiedName, out fullName, out outPath );
            SplitBaseFilename( fullName, out outBasename, out outExtention );
        }

        public static String MakeRelativePath( String workingDirectory, String fullPath ) {
            String result = String.Empty;
            int offset;
            // this is the easy case.  The file is inside of the working directory.
            if ( fullPath.StartsWith( workingDirectory ) ) {
                return fullPath.Substring( workingDirectory.Length + 1 );
            }
            // the hard case has to back out of the working directory
            String[] baseDirs = workingDirectory.Split( ':', '\\', '/' );
            String[] fileDirs = fullPath.Split( ':', '\\', '/' );

            // if we failed to split (empty strings?) or the drive letter does not match
            if ( baseDirs.Length <= 0 || fileDirs.Length <= 0 || baseDirs[ 0 ] != fileDirs[ 0 ] ) {
                // can't create a relative path between separate harddrives/partitions.
                return fullPath;
            }
            // skip all leading directories that match
            for ( offset = 1; offset < baseDirs.Length; offset++ ) {
                if ( baseDirs[ offset ] != fileDirs[ offset ] )
                    break;
            }
            // back out of the working directory
            for ( int i = 0; i < ( baseDirs.Length - offset ); i++ ) {
                result += "..\\";
            }
            // step into the file path
            for ( int i = offset; i < fileDirs.Length - 1; i++ ) {
                result += fileDirs[ i ] + "\\";
            }
            // append the file
            result += fileDirs[ fileDirs.Length - 1 ];
            return result;
        }

        public static String FormatMemorySize( int size ) {
            if ( size < 1024 ) {
                return String.Format( "{0} B", size );
            } else if ( size < 1024 * 1024 ) {
                return String.Format( "{0:f2} KB", size >> 10 );
            } else if ( size < 1024 * 1024 * 1024 ) {
                return String.Format( "{0:f2} MB", size >> 20 );
            } else {
                return String.Format( "{0:f2} GB", size >> 30 );
            }
        }

        public static String FormatMemorySize( long size ) {
            if ( size < 1024 ) {
                return String.Format( "{0} B", size );
            } else if ( size < 1024 * 1024 ) {
                return String.Format( "{0:f2} KB", size >> 10 );
            } else if ( size < 1024 * 1024 * 1024 ) {
                return String.Format( "{0:f2} MB", size >> 20 );
            } else if ( size < 1024L * 1024 * 1024 * 1024 ) {
                return String.Format( "{0:f2} GB", size >> 30 );
            } else {
                return String.Format( "{0:f2} TB", size >> 40 );
            }
        }
    }
}
