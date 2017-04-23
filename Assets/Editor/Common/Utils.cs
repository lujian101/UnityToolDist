using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Common {

    public static class Utils {

        static DateTime _epoch = new DateTime( 1970, 1, 1, 0, 0, 0, DateTimeKind.Utc );
        static long _epochTicks = _epoch.Ticks;
        static Stopwatch _globalTimer = new Stopwatch();
        static long _startupTicksMS = NowUnixTimeMS();

        static Utils() {
            _startupTicksMS = NowUnixTimeMS();
            _globalTimer.Start();
            ULogFile.sharedInstance.Log( "System Global Timer Started: {0}/ticks <=> {1}, IsHighResolution = {2}, Frequency = {3}",
                _startupTicksMS,
                UnixTimeMSToDateTime( _startupTicksMS ),
                Stopwatch.IsHighResolution,
                Stopwatch.Frequency
            );
        }

        public static int GetSystemTicksMS() {
            // TickCount cycles between Int32.MinValue, which is a negative 
            // number, and Int32.MaxValue once every 49.8 days. This sample
            // removes the sign bit to yield a nonnegative number that cycles 
            // between zero and Int32.MaxValue once every 24.9 days.
            return ( int )_globalTimer.ElapsedMilliseconds;
        }

        public static long GetSystemTicksMS64() {
            return _globalTimer.ElapsedMilliseconds;
        }

        public static int GetSystemTicksSec() {
            return ( int )( _globalTimer.ElapsedMilliseconds / 1000 );
        }

        public static long GetSystemTicksSec64() {
            return _globalTimer.ElapsedMilliseconds / 1000;
        }

        public static String GetFormatedLocalTime() {
            return System.DateTime.Now.ToString( "yyyy-MM-dd HH:mm:ss" );
        }

        public static long UnixTimeMSToTicks( long ms ) {
            return ms * TimeSpan.TicksPerMillisecond;
        }

        public static DateTime UnixTimeTicksToDateTime( long ticks ) {
            return ( _epoch + new TimeSpan( ticks ) ).ToLocalTime();
        }

        public static DateTime UnixTimeMSToDateTime( long ms ) {
            return UnixTimeTicksToDateTime( UnixTimeMSToTicks( ms ) );
        }

        public static long NowUnixTimeMS() {
            return ( DateTime.UtcNow.Ticks - _epochTicks ) / TimeSpan.TicksPerMillisecond;
        }
    }
}
