#define UNITY

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;
#if UNITY
using UnityEngine;
#endif

public class LogFile : IDisposable {

    public const String DefaultLogFileName = "default";
    public const String LogFolderNamePattern = @"^\d{4}-\d{2}-\d{2} \d{2}-\d\d-\d\d \(\d{4}\)$";
    public const int HowLongForStoreLogInHours = 7 * 24;

    public static String ROOT {
        get {
            return Application.persistentDataPath + "/Log";
        }
    }

    static String OutDir = String.Empty;

    static LogFile() {
        OutDir = ROOT + "/" + GetFormatedTimeText();
        UnityEngine.Debug.Log( "LogPath: " + OutDir );
        if ( !System.IO.Directory.Exists( OutDir ) ) {
            System.IO.Directory.CreateDirectory( OutDir );
        }
        var logCreatorInfo = OutDir + "/LogCreateInfo.txt";
        try {
            using ( var o = new StreamWriter( logCreatorInfo, false, Encoding.UTF8 ) ) {
                o.Write( Common.UDebug.GetCallStack( 2 ) );
            }
        } catch ( Exception e ) {
            Common.UDebug.LogError( e.ToString() );
        }
        ClearOldLogs();
    }

    class LogTextList : List<String> {
        int totalCount = 0;
        public new void Add( String item ) {
            base.Add( Common.StringUtils.SafeFormat( "<{0}> {1}\n{2}\n", totalCount, GetFormatedTimeText(), item ) );
            ++totalCount;
        }
    }

    class Group : IComparable<Group>, IDisposable {

        public String groupName = String.Empty;
        LogTextList m_content = new LogTextList();
        object m_locker = new object();

        String m_outFilePath = String.Empty;
        FileStream m_stream = null;
        StreamWriter m_writer = null;

        int m_ioErrorCount = 0;
        int m_streamCheck = 0;

        public Group( String name, String fullPath ) {
            groupName = name;
            m_outFilePath = fullPath;
            _CheckStream();
        }

        ~Group() {
            Dispose( false );
        }

        static bool IsFileInUse( String path ) {
            if ( File.Exists( path ) ) {
                try {
                    using ( var stream = new FileStream( path, FileMode.Open, FileAccess.Write ) ) { }
                } catch ( IOException ) {
                    return true;
                }
            }
            return false;
        }

        void _CheckStream() {
            if ( m_streamCheck == 0 ) {
                ++m_streamCheck;
                try {
                    if ( m_stream == null ) {
                        try {
                            m_stream = new FileStream( m_outFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite );
                            if ( m_stream != null ) {
                                m_writer = new StreamWriter( m_stream, Encoding.UTF8 );
                            }
                        } catch ( Exception e ) {
                            if ( m_ioErrorCount < 10 ) {
                                ++m_ioErrorCount;
                                UnityEngine.Debug.LogError( m_outFilePath );
                                UnityEngine.Debug.LogException( e );
                            }
                        }
                    }
                } finally {
                    --m_streamCheck;
                }
            }
        }

        public void CloseStream() {
            lock ( m_locker ) {
                if ( m_writer != null ) {
                    m_writer.Dispose();
                    m_writer = null;
                }
                if ( m_stream != null ) {
                    m_stream.Dispose();
                    m_stream = null;
                }
            }
        }

        void Dispose( bool disposing ) {
            if ( m_stream == null ) {
                return;
            }
            if ( disposing ) {
                // Free any other managed objects here.
                groupName = null;
                m_content = null;
                m_locker = null;
                m_outFilePath = null;
            }
            // Free any unmanaged objects here.
            if ( m_writer != null ) {
                m_writer.Dispose();
                m_writer = null;
            }
            m_stream.Dispose();
            m_stream = null;
        }

        public void Dispose() {
            if ( m_stream != null ) {
                Dispose( true );
                GC.SuppressFinalize( this );
            }
        }

        public int CompareTo( Group other ) {
            return groupName.CompareTo( other.groupName );
        }
        public void Add( String text ) {
            lock ( m_locker ) {
                m_content.Add( text );
            }
        }
        public void ToFile() {
            lock ( m_locker ) {
                _CheckStream();
                if ( m_content != null && m_writer != null ) {
                    for ( int i = 0; i < m_content.Count; ++i ) {
                        m_writer.WriteLine( m_content[i] );
                    }
                    m_content.Clear();
                }
                if ( m_writer != null ) {
                    m_writer.Flush();
                }
            }
        }
    }
    Thread m_workThread = null;
    AutoResetEvent m_event = new AutoResetEvent( false );
    object m_content_locker = new object();
    List<Group> m_content = new List<Group>();
    List<Group> m_content_tmp = new List<Group>();
    bool m_willExit = false;
    internal bool m_enable = true;
    bool m_stackTraceEnable = true;
    bool m_disposed = false;
    bool m_paused = false;

    public static long GetLogFileSizeInTotal( out int fileNum ) {
        long fileSize = 0;
        var files = Common.FileUtils.GetFileList(
            LogFile.ROOT,
            fn => {
                try {
                    var fi = new System.IO.FileInfo( fn );
                    fileSize += fi.Length;
                } catch ( System.IO.IOException e ) {
                    Common.UDebug.LogException( e );
                }
                return true;
            }
        );
        fileNum = files.Count;
        return fileSize;
    }

    public static void ClearOldLogs() {
        try {
            var today = DateTime.Now.ToString( "yyyy-MM-dd" );
            var folderList = Common.FileUtils.GetDirectoryList(
                LogFile.ROOT,
                dir => {
                    var s = dir.LastIndexOf( '/' );
                    if ( s != -1 ) {
                        var folderName = dir.Substring( s + 1 );
                        if ( folderName.StartsWith( today ) ) {
                            return true;
                        }
                        if ( !Regex.IsMatch( folderName, LogFolderNamePattern, RegexOptions.IgnoreCase ) ) {
                            return true;
                        }
                    }
                    return false;
                }
            );
            var now = DateTime.Now;
            for ( int i = 0; i < folderList.Count; ++i ) {
                var dirInfo = new DirectoryInfo( folderList[i] );
                var timeSpan = now - dirInfo.CreationTime;
                if ( timeSpan.TotalHours > HowLongForStoreLogInHours ) {
                    try {
                        Common.FileUtils.RemoveTree( folderList[i], true );
                    } catch ( Exception e ) {
                        Common.UDebug.LogException( e );
                    }
                }
            }
        } catch ( Exception e ) {
            Common.UDebug.LogException( e );
        }
    }

    public static String GetLogFilePath( String groupName, String suffix = null ) {
        return Common.StringUtils.SafeFormat( "{0}/{1}{2}.txt", OutDir,
            String.IsNullOrEmpty( groupName ) ? "default" : groupName, suffix ?? String.Empty );
    }

    public static String GetFormatedTimeText() {
        return DateTime.Now.ToString( "yyyy-MM-dd HH-mm-ss (ffff)" );
    }

    static public String AppendLogInfo( String source ) {
        return source;
    }

    public void LogException( Exception o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o.ToString() );
            UnityEngine.Debug.LogException( o );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogExceptionEx( String groupName, Exception o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o.ToString() );
            Common.UDebug.LogException( o );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void Log( String o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o );
            Common.UDebug.Log( text );
            AddLog( text );
        }
    }

    public void Log<T>( T o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o.ToString() );
            Common.UDebug.Log( text );
            AddLog( text );
        }
    }

    public void Log<T>( String format, T arg1 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1 ) );
            Common.UDebug.Log( text );
            AddLog( text );
        }
    }

    public void Log<T1, T2>( String format, T1 arg1, T2 arg2 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2 ) );
            Common.UDebug.Log( text );
            AddLog( text );
        }
    }

    public void Log<T1, T2, T3>( String format, T1 arg1, T2 arg2, T3 arg3 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2, arg3 ) );
            Common.UDebug.Log( text );
            AddLog( text );
        }
    }

    public void Log( String format, params object[] args ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, args ) );
            Common.UDebug.Log( text );
            AddLog( text );
        }
    }

    public void LogWarning( String o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogWarning<T>( T o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o.ToString() );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogWarning<T>( String format, T arg1 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1 ) );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogWarning<T1, T2>( String format, T1 arg1, T2 arg2 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2 ) );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogWarning<T1, T2, T3>( String format, T1 arg1, T2 arg2, T2 arg3 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2, arg3 ) );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogWarning( String format, params object[] args ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, args ) );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogError( String o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogError<T>( T o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o.ToString() );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogError<T>( String format, T arg1 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1 ) );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogError<T1, T2>( String format, T1 arg1, T2 arg2 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2 ) );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogError<T1, T2, T3>( String format, T1 arg1, T2 arg2, T3 arg3 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2, arg3 ) );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void LogError( String format, params object[] args ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, args ) );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ) );
            } else {
                AddLog( text );
            }
        }
    }

    public void GLog( String groupName, String o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o );
            Common.UDebug.Log( text );
            AddLog( text, groupName );
        }
    }

    public void GLog<T>( String groupName, T o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o.ToString() );
            Common.UDebug.Log( text );
            AddLog( text, groupName );
        }
    }

    public void GLog<T>( String groupName, String format, T arg1 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1 ) );
            Common.UDebug.Log( text );
            AddLog( text, groupName );
        }
    }

    public void GLog<T1, T2>( String groupName, String format, T1 arg1, T2 arg2 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2 ) );
            Common.UDebug.Log( text );
            AddLog( text, groupName );
        }
    }

    public void GLog<T1, T2, T3>( String groupName, String format, T1 arg1, T2 arg2, T3 arg3 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2, arg3 ) );
            Common.UDebug.Log( text );
            AddLog( text, groupName );
        }
    }

    public void GLog( String groupName, String format, params object[] args ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, args ) );
            Common.UDebug.Log( text );
            AddLog( text, groupName );
        }
    }

    public void GLogWarning( String groupName, String o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogWarning<T>( String groupName, T o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o.ToString() );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogWarning<T>( String groupName, String format, T arg1 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1 ) );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogWarning<T1, T2>( String groupName, String format, T1 arg1, T2 arg2 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2 ) );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogWarning<T1, T2, T3>( String groupName, String format, T1 arg1, T2 arg2, T2 arg3 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2, arg3 ) );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogWarning( String groupName, String format, params object[] args ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, args ) );
            Common.UDebug.LogWarning( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogError( String groupName, String o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogError<T>( String groupName, T o ) {
        if ( m_enable ) {
            var text = AppendLogInfo( o.ToString() );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogError<T>( String groupName, String format, T arg1 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1 ) );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogError<T1, T2>( String groupName, String format, T1 arg1, T2 arg2 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2 ) );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogError<T1, T2, T3>( String groupName, String format, T1 arg1, T2 arg2, T3 arg3 ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, arg1, arg2, arg3 ) );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void GLogError( String groupName, String format, params object[] args ) {
        if ( m_enable ) {
            var text = AppendLogInfo( Common.StringUtils.SafeFormat( format, args ) );
            UnityEngine.Debug.LogError( text );
            if ( m_stackTraceEnable ) {
                AddLog( Common.StringUtils.SafeFormat( "{0}\n{1}", text, Common.UDebug.GetCallStack( 2 ) ), groupName );
            } else {
                AddLog( text, groupName );
            }
        }
    }

    public void _UnityLogFile() {
        m_workThread = new Thread( TaskFun );
        m_workThread.Start();
        if ( !System.IO.Directory.Exists( ROOT ) ) {
            System.IO.Directory.CreateDirectory( ROOT );
        }
    }

    public void CloseStreams() {
        lock ( m_content_locker ) {
            if ( m_content_tmp != null ) {
                for ( int i = 0; i < m_content_tmp.Count; ++i ) {
                    m_content_tmp[i].CloseStream();
                }
            }
            if ( m_content != null ) {
                for ( int i = 0; i < m_content.Count; ++i ) {
                    m_content[i].CloseStream();
                }
            }
        }
    }

    public void Pause( bool pause ) {
        if ( m_paused != pause ) {
            m_paused = pause;
        }
    }

    public void Exit() {
        m_willExit = true;
        m_paused = false;
        m_event.Set();
        if ( m_workThread != null ) {
            m_workThread.Join( 1000 );
            m_workThread = null;
        }
        lock ( m_content_locker ) {
            if ( m_content_tmp != null ) {
                for ( int i = 0; i < m_content_tmp.Count; ++i ) {
                    m_content_tmp[i].Dispose();
                }
            }
            if ( m_content != null ) {
                for ( int i = 0; i < m_content.Count; ++i ) {
                    m_content[i].Dispose();
                }
            }
            m_content_tmp.Clear();
            m_content.Clear();
        }
    }

    int LowerBound( String key ) {
        int first = 0, middle;
        int half, len;
        len = m_content.Count;
        while ( len > 0 ) {
            half = len >> 1;
            middle = first + half;
            if ( m_content[middle].groupName.CompareTo( key ) < 0 ) {
                first = middle + 1;
                len = len - half - 1;
            } else {
                len = half;
            }
        }
        return first;
    }

    void AddLog( String text, String group = null ) {
        group = String.IsNullOrEmpty( group ) == false ? group : DefaultLogFileName;
        GetGroup( group ).Add( text );
        m_event.Set();
    }

    Group GetGroup( String name ) {
        Group ret = null;
        // binary search
        int index = LowerBound( name );
        if ( index != m_content.Count && m_content[index].groupName == name ) {
            ret = m_content[index];
        } else {
            ret = new Group( name, GetLogFilePath( name ) );
            lock ( m_content_locker ) {
                m_content.Insert( index, ret );
            }
        }
        return ret;
    }

    void TaskFun() {
        while ( !m_willExit ) {
            m_event.WaitOne();
            if ( !m_paused ) {
                m_content_tmp.Clear();
                lock ( m_content_locker ) {
                    for ( int i = 0; i < m_content.Count; ++i ) {
                        m_content_tmp.Add( m_content[i] );
                    }
                }
                for ( int i = 0; i < m_content_tmp.Count; ++i ) {
                    m_content_tmp[i].ToFile();
                }
            }
        }
    }

    ~LogFile() {
        Dispose( false );
    }

    public void Dispose() {
        if ( !m_disposed ) {
            Dispose( true );
            GC.SuppressFinalize( this );
        }
    }

    protected virtual void Dispose( bool disposing ) {
        if ( m_disposed ) {
            return;
        }
        // Free any unmanaged objects here.
        Exit();
        if ( disposing ) {
            // Free any other managed objects here.
            m_workThread = null;
            m_event = null;
            m_content_locker = null;
            m_content = null;
            m_content_tmp = null;
        }
        m_disposed = true;
        if ( UnityLogFile.m_logger == this ) {
            UnityLogFile.m_logger = null;
        }
    }

    public bool disposed {
        get {
            return m_disposed;
        }
    }
}

#if UNITY_EDITOR
[ExecuteInEditMode]
public class UnityLogFile : MonoBehaviour {
#else
public class UnityLogFile {
#endif

    internal static LogFile m_logger = null;

    public LogFile logger {
        get {
            return static_logger;
        }
    }

    internal static LogFile static_logger {
        get {
            if ( m_logger == null || m_logger.disposed ) {
                GC.Collect( 0 );
                GC.WaitForPendingFinalizers();
                m_logger = new LogFile();
                m_logger._UnityLogFile();
            }
            return m_logger;
        }
    }

    void Awake() {
        m_logger = new LogFile();
        m_logger._UnityLogFile();
    }

    void OnEnable() {
        if ( m_logger == null ) {
            m_logger = new LogFile();
            m_logger._UnityLogFile();
        }
        m_logger.m_enable = true;
    }

    void OnDisable() {
        if ( m_logger != null ) {
            m_logger.m_enable = false;
        }
    }

    void OnDestroy() {
        if ( m_logger != null ) {
            m_logger.Dispose();
            m_logger = null;
        }
    }
}

namespace Common {
    public static class ULogFile {
        public static LogFile sharedInstance {
            get {
                return UnityLogFile.static_logger;
            }
        }
    }
}
//EOF



