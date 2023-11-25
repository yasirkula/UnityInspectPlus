using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace InspectPlusNamespace
{
	// Delegates to get/set the value of a variable (field, property or method)
	public delegate object VariableGetVal( object obj );
	public delegate void VariableSetVal( object obj, object value );

	// Custom struct to hold a variable's description and its getter function
	public struct VariableGetterHolder : IComparable<VariableGetterHolder>
	{
		private static readonly Dictionary<Type, string> typeNamesLookup = new Dictionary<Type, string>
		{
			{ typeof(bool), "bool" },
			{ typeof(byte), "byte" },
			{ typeof(char), "char" },
			{ typeof(decimal), "decimal" },
			{ typeof(double), "double" },
			{ typeof(float), "float" },
			{ typeof(int), "int" },
			{ typeof(long), "long" },
			{ typeof(object), "object" },
			{ typeof(sbyte), "sbyte" },
			{ typeof(short), "short" },
			{ typeof(string), "string" },
			{ typeof(uint), "uint" },
			{ typeof(ulong), "ulong" },
			{ typeof(void), "void" }
		};

		public readonly string description;
		public readonly Type type;
		private readonly string name;
		private readonly VariableGetVal getter;
		private readonly VariableSetVal setter;

		public VariableGetterHolder( string description, Type type, VariableGetVal getter, VariableSetVal setter )
		{
			this.description = description;
			this.type = type;
			this.name = description;
			this.getter = getter;
			this.setter = setter;
		}

		public VariableGetterHolder( FieldInfo fieldInfo, VariableGetVal getter, VariableSetVal setter )
		{
			type = fieldInfo.FieldType;

			StringBuilder sb = Utilities.stringBuilder;
			sb.Length = 0;
			sb.Append( "(F" );

			if( fieldInfo.IsPublic )
				sb.Append( "+)" );
			else if( fieldInfo.IsFamily || fieldInfo.IsAssembly )
				sb.Append( "#)" );
			else
				sb.Append( "-)" );

			if( fieldInfo.IsStatic )
				sb.Append( "(S)" );
			if( Attribute.IsDefined( fieldInfo, typeof( ObsoleteAttribute ) ) )
				sb.Append( "(O)" );

			sb.Append( " " ).Append( fieldInfo.Name ).Append( " (" );
			AppendTypeToDescription( sb, type );
			sb.Append( ")" );

			this.description = sb.ToString();
			this.name = fieldInfo.Name;
			this.getter = getter;
			this.setter = setter;
		}

		public VariableGetterHolder( PropertyInfo propertyInfo, VariableGetVal getter, VariableSetVal setter )
		{
			type = propertyInfo.PropertyType;

			StringBuilder sb = Utilities.stringBuilder;
			sb.Length = 0;
			sb.Append( "(P" );

			MethodInfo getMethod = propertyInfo.GetGetMethod( true );
			if( getMethod.IsPublic )
				sb.Append( "+)" );
			else if( getMethod.IsFamily || getMethod.IsAssembly )
				sb.Append( "#)" );
			else
				sb.Append( "-)" );

			if( getMethod.IsStatic )
				sb.Append( "(S)" );
			if( Attribute.IsDefined( propertyInfo, typeof( ObsoleteAttribute ) ) )
				sb.Append( "(O)" );

			sb.Append( " " ).Append( propertyInfo.Name ).Append( " (" );
			AppendTypeToDescription( sb, type );
			sb.Append( ")" );

			this.description = sb.ToString();
			this.name = propertyInfo.Name;
			this.getter = getter;
			this.setter = setter;
		}

		public VariableGetterHolder( MethodInfo methodInfo, VariableGetVal getter )
		{
			type = methodInfo.ReturnType;

			StringBuilder sb = Utilities.stringBuilder;
			sb.Length = 0;
			sb.Append( "(M" );

			if( methodInfo.IsPublic )
				sb.Append( "+)" );
			else if( methodInfo.IsFamily || methodInfo.IsAssembly )
				sb.Append( "#)" );
			else
				sb.Append( "-)" );

			if( methodInfo.IsStatic )
				sb.Append( "(S)" );
			if( Attribute.IsDefined( methodInfo, typeof( ObsoleteAttribute ) ) )
				sb.Append( "(O)" );

			sb.Append( " " ).Append( methodInfo.Name ).Append( "() (" );
			AppendTypeToDescription( sb, type );
			sb.Append( ")" );

			this.description = sb.ToString();
			this.name = methodInfo.Name;
			this.getter = getter;
			this.setter = null;
		}

		private static void AppendTypeToDescription( StringBuilder sb, Type type )
		{
			string name;
			if( !typeNamesLookup.TryGetValue( type, out name ) )
				name = type.Name;

			if( !type.IsGenericType )
				sb.Append( name );
			else
			{
				int excludeIndex = name.IndexOf( '`' );
				if( excludeIndex > 0 )
					sb.Append( name, 0, excludeIndex );
				else
					sb.Append( name );

				sb.Append( "<" );

				Type[] arguments = type.GetGenericArguments();
				for( int i = 0; i < arguments.Length; i++ )
				{
					AppendTypeToDescription( sb, arguments[i] );

					if( i < arguments.Length - 1 )
						sb.Append( "," );
				}

				sb.Append( ">" );
			}
		}

		public object Get( object obj )
		{
			return getter( obj );
		}

		public void Set( object obj, object value )
		{
			if( setter != null )
				setter( obj, value );
		}

		int IComparable<VariableGetterHolder>.CompareTo( VariableGetterHolder other )
		{
			return name.CompareTo( other.name );
		}
	}

	// Credit: http://stackoverflow.com/questions/724143/how-do-i-create-a-delegate-for-a-net-property
	public interface IPropertyWrapper
	{
		object GetValue( object source );
		void SetValue( object source, object value );
	}

	// A wrapper class for properties to get/set their values more efficiently
	public class PropertyWrapper<TObject, TValue> : IPropertyWrapper where TObject : class
	{
		private readonly Func<TObject, TValue> getter;
		private readonly Action<TObject, TValue> setter;

		public PropertyWrapper( MethodInfo getterMethod, MethodInfo setterMethod )
		{
			getter = (Func<TObject, TValue>) Delegate.CreateDelegate( typeof( Func<TObject, TValue> ), getterMethod );
			if( setterMethod != null )
				setter = (Action<TObject, TValue>) Delegate.CreateDelegate( typeof( Action<TObject, TValue> ), setterMethod );
		}

		public object GetValue( object obj )
		{
			try
			{
				return getter( (TObject) obj );
			}
			catch
			{
				// Property getters may return various kinds of exceptions
				// if their backing fields are not initialized (yet)
				return null;
			}
		}

		public void SetValue( object obj, object value )
		{
			if( setter != null )
				setter( (TObject) obj, (TValue) value );
		}
	}

	// PropertyWrapper for static properties
	public class PropertyWrapper<TValue> : IPropertyWrapper
	{
		private readonly Func<TValue> getter;
		private readonly Action<TValue> setter;

		public PropertyWrapper( MethodInfo getterMethod, MethodInfo setterMethod )
		{
			getter = (Func<TValue>) Delegate.CreateDelegate( typeof( Func<TValue> ), getterMethod );
			if( setterMethod != null )
				setter = (Action<TValue>) Delegate.CreateDelegate( typeof( Action<TValue> ), setterMethod );
		}

		public object GetValue( object obj )
		{
			try
			{
				return getter();
			}
			catch
			{
				return null;
			}
		}

		public void SetValue( object obj, object value )
		{
			if( setter != null )
				setter( (TValue) value );
		}
	}

	// Using constant values in VariableGetterHolder
	public struct ConstantValueGetter
	{
		private readonly object value;

		public ConstantValueGetter( object value )
		{
			this.value = value;
		}

		public object GetValue( object obj )
		{
			return value;
		}
	}
}