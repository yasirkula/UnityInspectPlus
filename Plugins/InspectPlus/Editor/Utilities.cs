using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	internal static class Utilities
	{
		private const BindingFlags VARIABLE_BINDING_FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

		private static readonly HashSet<Type> primitiveUnityTypes = new HashSet<Type>()
		{
			typeof( string ), typeof( Vector4 ), typeof( Vector3 ), typeof( Vector2 ), typeof( Rect ),
			typeof( Quaternion ), typeof( Color ), typeof( Color32 ), typeof( Bounds ), typeof( Matrix4x4 ),
#if UNITY_2017_2_OR_NEWER
			typeof( Vector3Int ), typeof( Vector2Int ), typeof( RectInt ), typeof( BoundsInt )
#endif
		};

		private static readonly HashSet<string> obsoleteComponentAccessors = new HashSet<string>()
		{
			"rigidbody", "rigidbody2D", "camera", "light", "animation", "constantForce", "renderer", "audio", "guiText",
			"networkView", "guiElement", "guiTexture", "collider", "collider2D", "hingeJoint", "particleEmitter", "particleSystem"
		};

		private static readonly List<VariableGetterHolder> validVariables = new List<VariableGetterHolder>( 32 );
		private static readonly Dictionary<Type, VariableGetterHolder[]> typeToVariables = new Dictionary<Type, VariableGetterHolder[]>( 1024 );
		private static readonly string reflectionNameSpace = typeof( Assembly ).Namespace;
		public static readonly StringBuilder stringBuilder = new StringBuilder( 256 );

		private static MethodInfo screenFittedRectGetter;

		public static string GetDetailedObjectName( Object obj )
		{
			if( !obj )
				return "<null>";

			if( obj is GameObject )
			{
				Scene scene = ( (GameObject) obj ).scene;
				return scene.IsValid() ? string.Concat( scene.name, "/", obj.name, ".GameObject" ) : ( obj.name + " Asset.GameObject" );
			}
			else if( obj is Component )
			{
				Scene scene = ( (Component) obj ).gameObject.scene;
				return scene.IsValid() ? string.Concat( scene.name, "/", obj.name, ".", obj.GetType().Name ) : string.Concat( obj.name, " Asset.", obj.GetType().Name );
			}
			else if( obj is AssetImporter )
				return string.Concat( Path.GetFileNameWithoutExtension( ( (AssetImporter) obj ).assetPath ), " (", obj.GetType().Name, " Asset)" );
			else if( AssetDatabase.Contains( obj ) )
				return string.Concat( obj.name, " (", obj.GetType().Name, " Asset)" );
			else
			{
				string scenePath = AssetDatabase.GetAssetOrScenePath( obj );
				if( !string.IsNullOrEmpty( scenePath ) )
					return string.Concat( scenePath, "/", obj.name, " (", obj.GetType().Name, ")" );
				else
					return string.Concat( obj.name, " (", obj.GetType().Name, ")" );
			}
		}

		// Get filtered variables for a type
		public static VariableGetterHolder[] GetFilteredVariablesForType( Type type )
		{
			VariableGetterHolder[] result;
			if( typeToVariables.TryGetValue( type, out result ) )
				return result;

			validVariables.Clear();

			// Filter the variables
			Type currType = type;
			while( currType != typeof( object ) )
			{
				FieldInfo[] fields = currType.GetFields( VARIABLE_BINDING_FLAGS );
				for( int i = 0; i < fields.Length; i++ )
				{
					FieldInfo field = fields[i];
					Type variableType = field.FieldType;

					// Assembly variables can throw InvalidCastException on .NET 4.0 runtime
					if( typeof( Type ).IsAssignableFrom( variableType ) || variableType.Namespace == reflectionNameSpace )
						continue;

					// Pointers and ref variables can throw ArgumentException
					if( variableType.IsPointer || variableType.IsByRef )
						continue;

					VariableGetVal getter;
					VariableSetVal setter;
					field.CreateGetterAndSetter( out getter, out setter );
					if( getter != null )
						validVariables.Add( new VariableGetterHolder( field, getter, setter ) );
				}

				currType = currType.BaseType;
			}

			validVariables.Sort();
			int validVariablesPrevCount = validVariables.Count;

			currType = type;
			while( currType != typeof( object ) )
			{
				PropertyInfo[] properties = currType.GetProperties( VARIABLE_BINDING_FLAGS );
				for( int i = 0; i < properties.Length; i++ )
				{
					PropertyInfo property = properties[i];
					Type variableType = property.PropertyType;

					// Assembly variables can throw InvalidCastException on .NET 4.0 runtime
					if( typeof( Type ).IsAssignableFrom( variableType ) || variableType.Namespace == reflectionNameSpace )
						continue;

					// Pointers and ref variables can throw ArgumentException
					if( variableType.IsPointer || variableType.IsByRef )
						continue;

					// Skip properties without a getter function
					MethodInfo propertyGetter = property.GetGetMethod( true );
					if( propertyGetter == null )
						continue;

					// Skip indexer properties
					if( property.GetIndexParameters().Length > 0 )
						continue;

					// No need to check properties with 'override' keyword
					if( propertyGetter.GetBaseDefinition().DeclaringType != propertyGetter.DeclaringType )
						continue;

					// Additional filtering for properties:
					// Prevent accessing properties of Unity that instantiate an existing resource (causing memory leak)
					// Hide obsolete useless Component properties like "rigidbody", "camera", "collider" and so on
					string propertyName = property.Name;
					if( typeof( MeshFilter ).IsAssignableFrom( currType ) && propertyName == "mesh" )
						continue;
					else if( ( propertyName == "material" || propertyName == "materials" ) &&
						( typeof( Renderer ).IsAssignableFrom( currType ) || typeof( Collider ).IsAssignableFrom( currType ) || typeof( Collider2D ).IsAssignableFrom( currType ) ) )
						continue;
					else if( ( propertyName == "transform" || propertyName == "gameObject" ) &&
						( property.DeclaringType == typeof( Component ) || property.DeclaringType == typeof( GameObject ) ) )
						continue;
					else if( ( typeof( Component ).IsAssignableFrom( currType ) || typeof( GameObject ).IsAssignableFrom( currType ) ) &&
						Attribute.IsDefined( property, typeof( ObsoleteAttribute ) ) && obsoleteComponentAccessors.Contains( propertyName ) )
						continue;
					else
					{
						VariableGetVal getter;
						VariableSetVal setter;
						property.CreateGetterAndSetter( out getter, out setter );
						if( getter != null )
							validVariables.Add( new VariableGetterHolder( property, getter, setter ) );
					}
				}

				currType = currType.BaseType;
			}

			validVariables.Sort( validVariablesPrevCount, validVariables.Count - validVariablesPrevCount, null );
			validVariablesPrevCount = validVariables.Count;

			currType = type;
			while( currType != typeof( object ) )
			{
				MethodInfo[] methods = currType.GetMethods( VARIABLE_BINDING_FLAGS );
				for( int i = 0; i < methods.Length; i++ )
				{
					MethodInfo method = methods[i];

					// Skip operator overloads or property accessors
					if( method.IsSpecialName )
						continue;

					// Skip functions that take parameters or generic arguments (i.e. "GetComponent<Type>()")
					if( method.GetParameters().Length > 0 || method.GetGenericArguments().Length > 0 )
						continue;

					Type returnType = method.ReturnType;

					// Skip functions that don't return anything
					if( returnType == typeof( void ) )
						continue;

					// Assembly variables can throw InvalidCastException on .NET 4.0 runtime
					if( typeof( Type ).IsAssignableFrom( returnType ) || returnType.Namespace == reflectionNameSpace )
						continue;

					// Pointers and ref variables can throw ArgumentException
					if( returnType.IsPointer || returnType.IsByRef )
						continue;

					// No need to check methods with 'override' keyword
					if( method.GetBaseDefinition().DeclaringType != method.DeclaringType )
						continue;

					VariableGetVal getter = method.CreateGetter();
					if( getter != null )
						validVariables.Add( new VariableGetterHolder( method, getter ) );
				}

				currType = currType.BaseType;
			}

			validVariables.Sort( validVariablesPrevCount, validVariables.Count - validVariablesPrevCount, null );

			result = validVariables.ToArray();

			// Cache the filtered variables
			typeToVariables.Add( type, result );

			return result;
		}

		// Check if the type is a common Unity type (let's call them primitives)
		public static bool IsPrimitiveUnityType( this Type type )
		{
			return type.IsPrimitive || primitiveUnityTypes.Contains( type ) || type.IsEnum;
		}

		// Get <get> and <set> functions for a field
		private static void CreateGetterAndSetter( this FieldInfo fieldInfo, out VariableGetVal getter, out VariableSetVal setter )
		{
			getter = fieldInfo.GetValue;
			setter = ( !fieldInfo.IsInitOnly && !fieldInfo.IsLiteral ) ? fieldInfo.SetValue : (VariableSetVal) null;
		}

		// Get <get> and <set> functions for a property
		private static void CreateGetterAndSetter( this PropertyInfo propertyInfo, out VariableGetVal getter, out VariableSetVal setter )
		{
			// Can't use PropertyWrapper (which uses CreateDelegate) for property getters of structs
			if( propertyInfo.DeclaringType.IsValueType )
			{
				getter = propertyInfo.CanRead ? ( ( obj ) => propertyInfo.GetValue( obj, null ) ) : (VariableGetVal) null;
				setter = propertyInfo.CanWrite ? ( ( obj, value ) => propertyInfo.SetValue( obj, value, null ) ) : (VariableSetVal) null;
			}
			else
			{
				MethodInfo getMethod = propertyInfo.GetGetMethod( true );
				MethodInfo setMethod = propertyInfo.GetSetMethod( true );

				Type propertyWrapperType;
				if( !getMethod.IsStatic )
					propertyWrapperType = typeof( PropertyWrapper<,> ).MakeGenericType( propertyInfo.DeclaringType, propertyInfo.PropertyType );
				else
					propertyWrapperType = typeof( PropertyWrapper<> ).MakeGenericType( propertyInfo.PropertyType );

				IPropertyWrapper propertyWrapper = (IPropertyWrapper) Activator.CreateInstance( propertyWrapperType, getMethod, setMethod );
				getter = propertyWrapper.GetValue;
				setter = propertyWrapper.SetValue;
			}
		}

		// Get <get> function for a method
		private static VariableGetVal CreateGetter( this MethodInfo methodInfo )
		{
			// Can't use PropertyWrapper (which uses CreateDelegate) for methods of structs
			if( methodInfo.DeclaringType.IsValueType )
				return ( obj ) => methodInfo.Invoke( obj, null );

			if( !methodInfo.IsStatic )
			{
				Type GenType = typeof( PropertyWrapper<,> ).MakeGenericType( methodInfo.DeclaringType, methodInfo.ReturnType );
				return ( (IPropertyWrapper) Activator.CreateInstance( GenType, methodInfo, null ) ).GetValue;
			}
			else
			{
				Type GenType = typeof( PropertyWrapper<> ).MakeGenericType( methodInfo.ReturnType );
				return ( (IPropertyWrapper) Activator.CreateInstance( GenType, methodInfo, null ) ).GetValue;
			}
		}

		// Restricts the given Rect within the screen's bounds
		public static Rect GetScreenFittedRect( Rect originalRect )
		{
			if( screenFittedRectGetter == null )
				screenFittedRectGetter = typeof( EditorWindow ).Assembly.GetType( "UnityEditor.ContainerWindow" ).GetMethod( "FitRectToScreen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );

			return (Rect) screenFittedRectGetter.Invoke( null, new object[3] { originalRect, true, true } );
		}

		// Converts full paths to relative paths so that they can be used with AssetDatabase
		public static void ConvertAbsolutePathsToRelativePaths( string[] absolutePaths )
		{
			string projectPath = Path.GetFullPath( Directory.GetCurrentDirectory() );
			string projectPath2 = projectPath.Replace( '\\', '/' );

			int projectPathLength = projectPath2.Length;
			if( projectPath2[projectPath.Length - 1] != '/' )
				projectPathLength++;

			for( int i = 0; i < absolutePaths.Length; i++ )
			{
				if( absolutePaths[i].StartsWith( projectPath ) || absolutePaths[i].StartsWith( projectPath2 ) )
					absolutePaths[i] = absolutePaths[i].Substring( projectPathLength );
			}
		}
	}
}