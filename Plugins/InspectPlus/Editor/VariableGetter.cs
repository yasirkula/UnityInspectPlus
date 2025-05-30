﻿using System;
using System.Reflection;
using System.Text;

namespace InspectPlusNamespace
{
	// Delegates to get/set the value of a variable (field, property or method)
	public delegate object VariableGetVal( object obj );
	public delegate void VariableSetVal( object obj, object value );

	// Custom struct to hold a variable's description and its getter function
	public readonly struct VariableGetterHolder : IComparable<VariableGetterHolder>
	{
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

			sb.Append( " " ).Append( fieldInfo.Name ).Append( " (" ).AppendType( type ).Append( ")" );

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

			sb.Append( " " ).Append( propertyInfo.Name ).Append( " (" ).AppendType( type ).Append( ")" );

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

			sb.Append( " " ).Append( methodInfo.Name ).Append( "() (" ).AppendType( type ).Append( ")" );

			this.description = sb.ToString();
			this.name = methodInfo.Name;
			this.getter = getter;
			this.setter = null;
		}

		public readonly object Get( object obj )
		{
			return getter( obj );
		}

		public readonly void Set( object obj, object value )
		{
			if( setter != null )
				setter( obj, value );
		}

		readonly int IComparable<VariableGetterHolder>.CompareTo( VariableGetterHolder other )
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
	public readonly struct ConstantValueGetter
	{
		private readonly object value;

		public ConstantValueGetter( object value )
		{
			this.value = value;
		}

		public readonly object GetValue( object obj )
		{
			return value;
		}
	}
}