#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using CSharpExtensions;

namespace CSharpExtensions {
    public static class TypeExtensions {
        public static string ToGenericTypeString( this Type t ) {
            if ( !t.IsGenericType )
                return t.Name;
            string genericTypeName = t.GetGenericTypeDefinition().Name;
            genericTypeName = genericTypeName.Substring( 0,
                genericTypeName.IndexOf( '`' ) );
            string genericArgs = string.Join( ",",
                t.GetGenericArguments()
                    .Select( ta => ToGenericTypeString( ta ) ).ToArray() );
            return genericTypeName + "<" + genericArgs + ">";
        }
    }
}

public static class MonoScriptDumper {

    static Type[] BaseTypes = {
            typeof( bool ),
            typeof( byte ),
            typeof( sbyte ),
            typeof( short ),
            typeof( ushort ),
            typeof( int ),
            typeof( uint ),
            typeof( long ),
            typeof( ulong ),
            typeof( float ),
            typeof( double ),
            typeof( decimal ),
            typeof( string ),
        };

    static Dictionary<Type, String> typeAlias = new Dictionary<Type, String>();
    static HashSet<Type> BaseTypeSet = new HashSet<Type>();
    static Dictionary<Type, String> dumpCache = new Dictionary<Type, String>();
    static object locker = new object();

    static MonoScriptDumper() {
        for ( var i = 0; i < BaseTypes.Length; ++i ) {
            BaseTypeSet.Add( BaseTypes[ i ] );
        }
        
        typeAlias[ typeof( bool ) ] = "bool";
        typeAlias[ typeof( byte ) ] = "byte";
        typeAlias[ typeof( sbyte ) ] = "sbyte";
        typeAlias[ typeof( byte ) ] = "byte";
        typeAlias[ typeof( short ) ] = "short";
        typeAlias[ typeof( ushort ) ] = "ushort";
        typeAlias[ typeof( int ) ] = "int";
        typeAlias[ typeof( uint ) ] = "uint";
        typeAlias[ typeof( long ) ] = "long";
        typeAlias[ typeof( ulong ) ] = "ulong";
        typeAlias[ typeof( float ) ] = "float";
        typeAlias[ typeof( double ) ] = "double";
        typeAlias[ typeof( decimal ) ] = "decimal";
        typeAlias[ typeof( string ) ] = "string";
    }

    static String GetAlias( Type t ) {
        String o;
        if ( typeAlias.TryGetValue( t, out o ) ) {
            return o;
        }
        if ( t.Namespace == "UnityEngine" ) {
            return t.Name;
        }
        return t.FullName;
    }

    static bool DumpType( Type ftype, String name, out String prettyTypeName, int indent = 0, HashSet<Type> customTypes = null, bool isRoot = false ) {
        customTypes = customTypes ?? new HashSet<Type>();
        prettyTypeName = String.Empty;
        if ( typeof( Delegate ).IsAssignableFrom( ftype ) ) {
            return false;
        }
        if ( BaseTypeSet.Contains( ftype ) ||
            ftype.IsEnum ) {
            prettyTypeName = String.Format( "{0}{1} {2}\n",
                new String( '\t', indent ),
                GetAlias( ftype ), name );
            return true;
        } else if ( ftype.IsArray ) {
            var et = ftype.GetElementType();
            if ( et != null ) {
                var pname = String.Empty;
                if ( DumpType( et, "", out pname, indent + 1, customTypes ) ) {
                    prettyTypeName = String.Format( "{0}{1}[] {2}\n",
                        new String( '\t', indent ),
                        GetAlias( et ), name );
                    if ( !BaseTypeSet.Contains( et ) && et.IsEnum == false ) {
                        prettyTypeName = prettyTypeName + pname;
                    }
                    return true;
                }
            }
        } else if ( ftype.IsGenericType ) {
            var listType = typeof( List<> );
            var gargs = ftype.GetGenericArguments();
            if ( gargs.Length == 1 ) {
                if ( ftype == listType.MakeGenericType( gargs ) ) {
                    var pname = String.Empty;
                    if ( DumpType( gargs[ 0 ], "T", out pname, indent + 1, customTypes ) ) {
                        prettyTypeName = String.Format( "{0}List<{1}> {2}\n",
                            new String( '\t', indent ),
                            GetAlias( gargs[ 0 ] ), name );
                        if ( !BaseTypeSet.Contains( gargs[ 0 ] ) && gargs[ 0 ].IsEnum == false ) {
                            prettyTypeName = prettyTypeName + pname;
                        }
                        return true;
                    }
                }
            }
        } else if ( !isRoot && typeof( UnityEngine.Object ).IsAssignableFrom( ftype ) ) {
            // unity's object will be serialized as a pointer/GUID
            prettyTypeName = String.Format( "{0}{1} {2}\n",
                new String( '\t', indent ),
                GetAlias( ftype ), name );
            return true;
        } else if ( ftype.IsClass || ( ftype.IsValueType && !ftype.IsEnum ) ) {
            if ( !ftype.IsEnum ) {
                var attrs = System.Attribute.GetCustomAttributes( ftype );
                var nserTag = Array.FindIndex( attrs, a => a is SerializableAttribute );
                if ( ftype.IsClass && nserTag == -1 && !typeof( UnityEngine.Object ).IsAssignableFrom( ftype ) ) {
                    return false;
                }
            }
            prettyTypeName = String.Format( "{0}{1} {2}\n",
                new String( '\t', indent ),
                GetAlias( ftype ), name );
            FieldInfo[] fields = null;
            if ( !customTypes.Contains( ftype ) ) {
                fields = ftype.GetFields(
                    BindingFlags.Instance |
                    BindingFlags.NonPublic |
                    BindingFlags.Public |
                    BindingFlags.FlattenHierarchy );
                customTypes.Add( ftype );
            }
            if ( fields != null && fields.Length > 0 ) {
                var sb = new StringBuilder();
                for ( int i = 0; i < fields.Length; ++i ) {
                    var fi = fields[ i ];
                    var _ftype = fi.FieldType;
                    var attrs = Attribute.GetCustomAttributes( fi );
                    var nserTag = Array.FindIndex( attrs, a => a is NonSerializedAttribute );
                    if ( nserTag != -1 ) {
                        continue;
                    }
                    var serTag = Array.FindIndex( attrs, a => a is SerializeField );
                    if ( !( serTag != -1 || fi.IsPublic ) ) {
                        continue;
                    }
                    var pname = String.Empty;
                    if ( DumpType( _ftype, fi.Name, out pname, indent + 1, customTypes ) ) {
                        sb.Append( pname );
                    }
                }
                prettyTypeName = prettyTypeName + sb.ToString();
            }
            return true;
        }
        return false;
    }

    public static void Clear() {
        lock ( locker ) {
            dumpCache.Clear();
        }
    }

    public static String Dump( Type type ) {
        var content = String.Empty;
        lock ( locker ) {
            if ( !dumpCache.TryGetValue( type, out content ) ) {
                DumpType( type, "", out content, 0, null, true );
                if ( !String.IsNullOrEmpty( content ) ) {
                    dumpCache.Add( type, content );
                }
            }
        }
        return content;
    }
}
#endif
//EOF
