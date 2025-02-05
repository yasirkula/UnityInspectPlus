using System;
using UnityEngine;

namespace InspectPlusNamespace
{
	public class TypeWrapper : ScriptableObject, ISerializationCallbackReceiver
	{
		public Type Type { get; private set; }

		[SerializeField, HideInInspector]
		private string typeName;

		public static TypeWrapper Create( Type type )
		{
			TypeWrapper result = CreateInstance<TypeWrapper>();
			result.name = type.Name + " Statics";
			result.Type = type;
			result.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
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