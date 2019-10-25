using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace InspectPlusNamespace.Extras
{
	public static class Utilities
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

					// Pointer variables can throw ArgumentException
					if( variableType.IsPointer )
						continue;

					VariableGetVal getter = field.CreateGetter();
					if( getter != null )
						validVariables.Add( new VariableGetterHolder( field, getter ) );
				}

				currType = currType.BaseType;
			}

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

					// Pointer variables can throw ArgumentException
					if( variableType.IsPointer )
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
						VariableGetVal getter = property.CreateGetter();
						if( getter != null )
							validVariables.Add( new VariableGetterHolder( property, getter ) );
					}
				}

				currType = currType.BaseType;
			}

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

		// Get <get> function for a field
		public static VariableGetVal CreateGetter( this FieldInfo fieldInfo )
		{
			return fieldInfo.GetValue;
		}

		// Get <get> function for a property
		public static VariableGetVal CreateGetter( this PropertyInfo propertyInfo )
		{
			// Can't use PropertyWrapper (which uses CreateDelegate) for property getters of structs
			if( propertyInfo.DeclaringType.IsValueType )
				return propertyInfo.CanRead ? ( ( obj ) => propertyInfo.GetValue( obj, null ) ) : (VariableGetVal) null;

			MethodInfo getMethod = propertyInfo.GetGetMethod( true );
			if( !getMethod.IsStatic )
			{
				Type GenType = typeof( PropertyWrapper<,> ).MakeGenericType( propertyInfo.DeclaringType, propertyInfo.PropertyType );
				return ( (IPropertyAccessor) Activator.CreateInstance( GenType, getMethod ) ).GetValue;
			}
			else
			{
				Type GenType = typeof( PropertyWrapper<> ).MakeGenericType( propertyInfo.PropertyType );
				return ( (IPropertyAccessor) Activator.CreateInstance( GenType, getMethod ) ).GetValue;
			}
		}
	}
}