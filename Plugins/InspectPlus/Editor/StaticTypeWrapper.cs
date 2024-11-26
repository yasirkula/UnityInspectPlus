using System;
using UnityEngine;

namespace InspectPlusNamespace
{
	public class StaticTypeWrapper : ScriptableObject, ISerializationCallbackReceiver
	{
		public Type Type { get; private set; }

		[SerializeField, HideInInspector]
		private string typeName;

		public static StaticTypeWrapper Create( Type type )
		{
			StaticTypeWrapper result = CreateInstance<StaticTypeWrapper>();
			result.name = type.Name + " Statics";
			result.Type = type;
			return result;
		}

		void ISerializationCallbackReceiver.OnBeforeSerialize()
		{
			if( Type != null )
				typeName = Type.AssemblyQualifiedName;
		}

		void ISerializationCallbackReceiver.OnAfterDeserialize()
		{
			if( !string.IsNullOrEmpty( typeName ) )
				Type = Type.GetType( typeName );
		}
	}
}