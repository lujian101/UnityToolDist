using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System;
using UnityEngine;
using UnityEditor;

static class MaterialCleaner {

    public static bool ClearMaterialAsset( Material m ) {
        if ( m == null ) {
            return false;
        }
        var path = AssetDatabase.GetAssetPath( m );
        if ( String.IsNullOrEmpty( path ) ) {
            return false;
        }
        var deps = AssetDatabase.GetDependencies( new String[] { path } );
        var deps_textures = deps.Where( s => IsTextureAsset( s ) ).ToList();
        var used_textures = new HashSet<String>();
        var shader = m.shader;
        var newMat = new Material( shader );
        var c = ShaderUtil.GetPropertyCount( shader );
        for ( int i = 0; i < c; ++i ) {
            var type = ShaderUtil.GetPropertyType( shader, i );
            var name = ShaderUtil.GetPropertyName( shader, i );
            var value = m.GetProperty( i );
            switch ( type ) {
            case ShaderUtil.ShaderPropertyType.Color: {
                    newMat.SetColor( name, m.GetColor( name ) );
                }
                break;
            case ShaderUtil.ShaderPropertyType.Float: {
                    newMat.SetFloat( name, m.GetFloat( name ) );
                }
                break;
            case ShaderUtil.ShaderPropertyType.Range: {
                    newMat.SetFloat( name, ( float )value );
                }
                break;
            case ShaderUtil.ShaderPropertyType.TexEnv: {
                    newMat.SetTexture( name, ( Texture )value );
                    newMat.SetTextureOffset( name, m.GetTextureOffset( name ) );
                    newMat.SetTextureScale( name, m.GetTextureScale( name ) );
                    var tpath = AssetDatabase.GetAssetPath( ( Texture )value );
                    if ( !String.IsNullOrEmpty( tpath ) ) {
                        used_textures.Add( tpath );
                    }
                }
                break;
            case ShaderUtil.ShaderPropertyType.Vector: {
                    newMat.SetVector( name, ( Vector4 )value );
                }
                break;
            }
        }
        bool rebuild = false;
        if ( used_textures.Count != deps_textures.Count ) {
            for ( int i = 0; i < deps_textures.Count; ++i ) {
                var _fn = deps_textures[ i ];
                if ( !used_textures.Contains( _fn ) ) {
                    rebuild = true;
                    UnityEngine.Debug.LogWarning( String.Format( "unused texture: {0}", _fn ) );
                }
            }
        }
        if ( !rebuild ) {
            if ( newMat != null ) {
                UnityEngine.Object.DestroyImmediate( newMat );
            }
            return false;
        }
        String basePath;
        String fn;
        String ext;
        SplitFullFilename( path, out fn, out ext, out basePath );
        var tempAssetPath = String.Format( "{0}{1}_temp.{2}", basePath, fn, ext );
        var _test = AssetDatabase.LoadAllAssetsAtPath( tempAssetPath );
        if ( _test != null ) {
            AssetDatabase.DeleteAsset( tempAssetPath );
        }
        // create a new material to replace it latter
        AssetDatabase.CreateAsset( newMat, tempAssetPath );
        Resources.UnloadAsset( newMat );
        var tempAssetDataPath = String.Format( "{0}{1}_datatemp.bytes", basePath, fn, ext );
        if ( File.Exists( tempAssetPath ) ) {
            // rename it to .bytes
            File.Copy( tempAssetPath, tempAssetDataPath, true );
            // delete temp material
            AssetDatabase.DeleteAsset( tempAssetPath );
            if ( File.Exists( tempAssetDataPath ) ) {
                // delete original material
                File.Delete( path );
                // replace original material with .bytes file
                File.Copy( tempAssetDataPath, path, true );
                // remove bytes file
                File.Delete( tempAssetDataPath );
                AssetDatabase.Refresh();
                // make sure the temp file has been removed correctly
                if ( File.Exists( tempAssetDataPath ) ) {
                    UnityEngine.Debug.Log( String.Format( "AssetDatabase.DeleteAsset failed: {0}", tempAssetDataPath ) );
                    File.Delete( tempAssetDataPath );
                    AssetDatabase.Refresh();
                    return true;
                }
            }
        }
        return false;
    }

    static void SplitFilename( String qualifiedName, out String outBasename, out String outPath ) {
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

    static void SplitBaseFilename( String fullName, out String outBasename, out String outExtention ) {
        int i = fullName.LastIndexOf( '.' );
        if ( i == -1 ) {
            outExtention = String.Empty;
            outBasename = fullName;
        } else {
            outExtention = fullName.Substring( i + 1 );
            outBasename = fullName.Substring( 0, i );
        }
    }

    static void SplitFullFilename( String qualifiedName, out String outBasename, out String outExtention, out String outPath ) {
        String fullName = String.Empty;
        SplitFilename( qualifiedName, out fullName, out outPath );
        SplitBaseFilename( fullName, out outBasename, out outExtention );
    }

    static object GetProperty( this Material material, int index ) {
        var name = ShaderUtil.GetPropertyName( material.shader, index );
        var type = ShaderUtil.GetPropertyType( material.shader, index );
        switch ( type ) {
        case ShaderUtil.ShaderPropertyType.Color:
            return material.GetColor( name );
        case ShaderUtil.ShaderPropertyType.Vector:
            return material.GetVector( name );
        case ShaderUtil.ShaderPropertyType.Range:
        case ShaderUtil.ShaderPropertyType.Float:
            return material.GetFloat( name );
        case ShaderUtil.ShaderPropertyType.TexEnv:
            return material.GetTexture( name );
        }
        return null;
    }

    static bool IsTextureAsset( String assetPath ) {
        var ext = Path.GetExtension( assetPath ).ToLower();
        return ext == ".png" ||
            ext == ".tga" ||
            ext == ".jpg" ||
            ext == ".bmp" ||
            ext == ".psd" ||
            ext == ".dds" ||
            ext == ".exr";
    }
}

//EOF
