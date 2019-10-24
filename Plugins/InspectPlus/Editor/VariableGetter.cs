using System;
using System.Reflection;
using System.Text;

namespace InspectPlusNamespace.Extras
{
	// Delegate to get the value of a variable (either field or property)
	public delegate object VariableGetVal( object obj );

	// Custom struct to hold a variable, its important properties and its getter function
	public struct VariableGetterHolder
	{
		public readonly string description;
		private readonly VariableGetVal getter;

		public VariableGetterHolder( string description, VariableGetVal getter )
		{
			this.description = description;
			this.getter = getter;
		}

		public VariableGetterHolder( FieldInfo fieldInfo, VariableGetVal getter )
		{
			Type type = fieldInfo.FieldType;

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

			sb.Append( "(" );
			AppendTypeToDescription( sb, type );
			sb.Append( ") " );
			sb.Append( fieldInfo.Name );

			this.description = sb.ToString();
			this.getter = getter;
		}

		public VariableGetterHolder( PropertyInfo propertyInfo, VariableGetVal getter )
		{
			Type type = propertyInfo.PropertyType;
			MethodInfo getMethod = propertyInfo.GetGetMethod( true );

			StringBuilder sb = Utilities.stringBuilder;
			sb.Length = 0;
			sb.Append( "(P" );

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

			sb.Append( "(" );
			AppendTypeToDescription( sb, type );
			sb.Append( ") " );
			sb.Append( propertyInfo.Name );

			this.description = sb.ToString();
			this.getter = getter;
		}

		private static void AppendTypeToDescription( StringBuilder sb, Type type )
		{
			string name = type.Name;
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
	}

	// Credit: http://stackoverflow.com/questions/724143/how-do-i-create-a-delegate-for-a-net-property
	public interface IPropertyAccessor
	{
		object GetValue( object source );
	}

	// A wrapper class for properties to get their values more efficiently
	public class PropertyWrapper<TObject, TValue> : IPropertyAccessor where TObject : class
	{
		private readonly Func<TObject, TValue> getter;

		public PropertyWrapper( MethodInfo getterMethod )
		{
			getter = (Func<TObject, TValue>) Delegate.CreateDelegate( typeof( Func<TObject, TValue> ), getterMethod );
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
	}

	// PropertyWrapper for static properties
	public class PropertyWrapper<TValue> : IPropertyAccessor
	{
		private readonly Func<TValue> getter;

		public PropertyWrapper( MethodInfo getterMethod )
		{
			getter = (Func<TValue>) Delegate.CreateDelegate( typeof( Func<TValue> ), getterMethod );
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