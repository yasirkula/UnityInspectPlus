using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InspectPlusNamespace
{
	internal static class MenuItems
	{
		internal const string NEW_TAB_LABEL = "Open In New Tab";
		internal const string NEW_WINDOW_LABEL = "Open In New Window";

		private const string CONTEXT_COPY_LABEL = "Copy (Inspect+)";
		private const string CONTEXT_COPY_COMPONENT_LABEL = "Copy Component (Inspect+)";
		private const string CONTEXT_COPY_VALUE_LABEL = "Copy Value";
		private const string CONTEXT_PASTE_LABEL = "Paste (Inspect+)";
		private const string CONTEXT_PASTE_VALUES_LABEL = "Paste Values (Inspect+)";
		private const string CONTEXT_PASTE_COMPONENT_VALUES_LABEL = "Paste Component Values (Inspect+)";
		private const string CONTEXT_PASTE_COMPONENT_AS_NEW_LABEL = "Paste Component As New (Inspect+)";
		private const string CONTEXT_PASTE_FROM_BIN_LABEL = "Paste From Bin (Inspect+)";
		private const string CONTEXT_PASTE_VALUES_FROM_BIN_LABEL = "Paste Values From Bin (Inspect+)";
		private const string CONTEXT_PASTE_COMPONENT_VALUES_FROM_BIN_LABEL = "Paste Component Values From Bin (Inspect+)";
		private const string CONTEXT_PASTE_COMPONENT_AS_NEW_FROM_BIN_LABEL = "Paste Component As New From Bin (Inspect+)";

		private static List<Object> objectsToOpenPasteBinWith;

		#region Context Menu Buttons
		[MenuItem( "GameObject/Inspect+/" + NEW_TAB_LABEL, priority = 49 )]
		[MenuItem( "Assets/Inspect+/" + NEW_TAB_LABEL, priority = 1500 )]
		private static void MenuItemNewTab( MenuCommand command )
		{
			if( command.context )
				InspectPlusWindow.Inspect( PreferablyGameObject( command.context ), false );
			else
				InspectPlusWindow.Inspect( PreferablyGameObject( Selection.objects ), false );
		}

		[MenuItem( "GameObject/Inspect+/" + NEW_WINDOW_LABEL, priority = 49 )]
		[MenuItem( "Assets/Inspect+/" + NEW_WINDOW_LABEL, priority = 1500 )]
		private static void MenuItemNewWindow( MenuCommand command )
		{
			if( command.context )
				InspectPlusWindow.Inspect( PreferablyGameObject( command.context ), true );
			else
				InspectPlusWindow.Inspect( PreferablyGameObject( Selection.objects ), true );
		}

		[MenuItem( "CONTEXT/Component/" + NEW_TAB_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + NEW_TAB_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/AssetImporter/" + NEW_TAB_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/Material/" + NEW_TAB_LABEL, priority = 1500 )]
		private static void ContextMenuItemNewTab( MenuCommand command )
		{
			InspectPlusWindow.Inspect( command.context, false );
		}

		[MenuItem( "CONTEXT/Component/" + NEW_WINDOW_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + NEW_WINDOW_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/AssetImporter/" + NEW_WINDOW_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/Material/" + NEW_WINDOW_LABEL, priority = 1500 )]
		private static void ContextMenuItemNewWindow( MenuCommand command )
		{
			InspectPlusWindow.Inspect( command.context, true );
		}

		[MenuItem( "GameObject/Inspect+/" + CONTEXT_COPY_VALUE_LABEL, priority = 49 )]
		[MenuItem( "Assets/Inspect+/" + CONTEXT_COPY_VALUE_LABEL, priority = 1500 )]
		[MenuItem( "CONTEXT/Component/" + CONTEXT_COPY_COMPONENT_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_COPY_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_COPY_LABEL, priority = 1450 )]
		private static void ContextMenuItemCopyObject( MenuCommand command )
		{
			// Passing null as context parameter because we don't want to calculate a "./" RelativePath for this clipboard in XML mode
			if( command.context )
				PasteBinWindow.AddToClipboard( command.context, Utilities.GetDetailedObjectName( command.context ), null );
			else
				PasteBinWindow.AddToClipboard( Selection.activeObject, Utilities.GetDetailedObjectName( Selection.activeObject ), null );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_COMPONENT_AS_NEW_LABEL, priority = 1450 )]
		private static void ContextMenuItemPasteComponentAsNew( MenuCommand command )
		{
			if( PasteBinWindow.ActiveClipboard != null )
				PasteBinWindow.ActiveClipboard.PasteAsNewComponent( command.context );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_COMPONENT_AS_NEW_FROM_BIN_LABEL, priority = 1450 )]
		private static void ContextMenuItemPasteComponentAsNewFromBin( MenuCommand command )
		{
			// See ContextMenuItemPasteObjectFromBin for the purpose of EditorApplication.update here
			if( command.context )
			{
				if( objectsToOpenPasteBinWith == null )
					objectsToOpenPasteBinWith = new List<Object>( 2 ) { command.context };
				else
					objectsToOpenPasteBinWith.Add( command.context );

				EditorApplication.update -= CallPastePasteComponentAsNewFromBin;
				EditorApplication.update += CallPastePasteComponentAsNewFromBin;
			}
			else if( objectsToOpenPasteBinWith != null )
			{
				PasteComponentAsNewFromBin( objectsToOpenPasteBinWith.ToArray() );
				objectsToOpenPasteBinWith = null;
			}
		}

		private static void CallPastePasteComponentAsNewFromBin()
		{
			EditorApplication.update -= CallPastePasteComponentAsNewFromBin;
			ContextMenuItemPasteComponentAsNewFromBin( new MenuCommand( null ) );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_COMPONENT_VALUES_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_PASTE_VALUES_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_PASTE_VALUES_LABEL, priority = 1450 )]
		private static void ContextMenuItemPasteObject( MenuCommand command )
		{
			if( PasteBinWindow.ActiveClipboard != null )
				PasteBinWindow.ActiveClipboard.PasteToObject( command.context );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_COMPONENT_VALUES_FROM_BIN_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_PASTE_VALUES_FROM_BIN_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_PASTE_VALUES_FROM_BIN_LABEL, priority = 1450 )]
		private static void ContextMenuItemPasteObjectFromBin( MenuCommand command )
		{
			// This happens when this button is clicked while multiple Objects were selected. In this case,
			// this function will be called once for each selected Object. We don't want to open a separate
			// paste bin window for each selected Object. Instead, show a single paste bin window that will
			// paste to all of the selected Objects. We aren't using Selection.objects because for components,
			// it will return the GameObject instead
			if( command.context )
			{
				if( objectsToOpenPasteBinWith == null )
					objectsToOpenPasteBinWith = new List<Object>( 2 ) { command.context };
				else
					objectsToOpenPasteBinWith.Add( command.context );

				EditorApplication.update -= CallPasteObjectFromBinOnce;
				EditorApplication.update += CallPasteObjectFromBinOnce;
			}
			else if( objectsToOpenPasteBinWith != null )
			{
				PasteValueFromBin( objectsToOpenPasteBinWith.ToArray() );
				objectsToOpenPasteBinWith = null;
			}
		}

		private static void CallPasteObjectFromBinOnce()
		{
			EditorApplication.update -= CallPasteObjectFromBinOnce;
			ContextMenuItemPasteObjectFromBin( new MenuCommand( null ) );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_COMPONENT_AS_NEW_LABEL, validate = true )]
		private static bool ContextMenuItemPasteComponentAsNewValidate( MenuCommand command )
		{
			return ValidatePasteOperation( command ) && PasteBinWindow.ActiveClipboard != null && PasteBinWindow.ActiveClipboard.CanPasteAsNewComponent( command.context );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_COMPONENT_AS_NEW_FROM_BIN_LABEL, validate = true )]
		private static bool ContextMenuItemPasteComponentAsNewFromBinValidate( MenuCommand command )
		{
			return ValidatePasteOperation( command );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_COMPONENT_VALUES_LABEL, validate = true )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_PASTE_VALUES_LABEL, validate = true )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_PASTE_VALUES_LABEL, validate = true )]
		private static bool ContextMenuItemPasteObjectValidate( MenuCommand command )
		{
			return ValidatePasteOperation( command ) && PasteBinWindow.ActiveClipboard != null && PasteBinWindow.ActiveClipboard.CanPasteToObject( command.context );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_COMPONENT_VALUES_FROM_BIN_LABEL, validate = true )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_PASTE_VALUES_FROM_BIN_LABEL, validate = true )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_PASTE_VALUES_FROM_BIN_LABEL, validate = true )]
		private static bool ContextMenuItemPasteObjectFromBinValidate( MenuCommand command )
		{
			return ValidatePasteOperation( command );
		}

		private static bool ValidatePasteOperation( MenuCommand command )
		{
			if( !command.context )
			{
				Debug.LogError( "Encountered empty context, probably a missing script." );
				return false;
			}

			if( ( command.context.hideFlags & HideFlags.NotEditable ) == HideFlags.NotEditable )
				return false;

			return true;
		}

		[MenuItem( "GameObject/Inspect+/" + NEW_TAB_LABEL, validate = true )]
		[MenuItem( "GameObject/Inspect+/" + NEW_WINDOW_LABEL, validate = true )]
		[MenuItem( "Assets/Inspect+/" + NEW_TAB_LABEL, validate = true )]
		[MenuItem( "Assets/Inspect+/" + NEW_WINDOW_LABEL, validate = true )]
		private static bool GameObjectMenuValidate( MenuCommand command )
		{
			return Selection.objects.Length > 0;
		}

		public static void OnPropertyRightClicked( GenericMenu menu, SerializedProperty property )
		{
			Object obj = null;
			bool isUnityObjectType = false;
			if( property.propertyType == SerializedPropertyType.ExposedReference )
			{
				obj = property.exposedReferenceValue;
				isUnityObjectType = true;
			}
			else if( property.propertyType == SerializedPropertyType.ObjectReference )
			{
				obj = property.objectReferenceValue;
				isUnityObjectType = true;
			}

			if( isUnityObjectType && property.hasMultipleDifferentValues )
			{
				string propertyPath = property.propertyPath;
				Object[] targets = property.serializedObject.targetObjects;

				bool containsComponents = false;
				for( int i = 0; i < targets.Length; i++ )
				{
					SerializedProperty _property = new SerializedObject( targets[i] ).FindProperty( propertyPath );
					if( _property.propertyType == SerializedPropertyType.ExposedReference )
					{
						targets[i] = _property.exposedReferenceValue;
						if( targets[i] is Component )
							containsComponents = true;
					}
					else if( _property.propertyType == SerializedPropertyType.ObjectReference )
					{
						targets[i] = _property.objectReferenceValue;
						if( targets[i] is Component )
							containsComponents = true;
					}
				}

				if( containsComponents )
				{
					menu.AddItem( new GUIContent( NEW_TAB_LABEL + "/All/GameObject" ), false, () => InspectPlusWindow.Inspect( PreferablyGameObject( targets ), false ) );
					menu.AddItem( new GUIContent( NEW_TAB_LABEL + "/All/Component" ), false, () => InspectPlusWindow.Inspect( targets, false ) );
					menu.AddItem( new GUIContent( NEW_WINDOW_LABEL + "/All/GameObject" ), false, () => InspectPlusWindow.Inspect( PreferablyGameObject( targets ), true ) );
					menu.AddItem( new GUIContent( NEW_WINDOW_LABEL + "/All/Component" ), false, () => InspectPlusWindow.Inspect( targets, true ) );
				}
				else
				{
					menu.AddItem( new GUIContent( NEW_TAB_LABEL + "/All" ), false, () => InspectPlusWindow.Inspect( targets, false ) );
					menu.AddItem( new GUIContent( NEW_WINDOW_LABEL + "/All" ), false, () => InspectPlusWindow.Inspect( targets, true ) );
				}

				for( int i = 0; i < targets.Length; i++ )
				{
					if( targets[i] )
						AddInspectButtonToMenu( menu, targets[i], "/" + targets[i].name );
				}

				menu.AddSeparator( "" );
			}
			else if( obj )
			{
				AddInspectButtonToMenu( menu, obj, "" );
				menu.AddSeparator( "" );
			}

			if( !property.hasMultipleDifferentValues && ( !isUnityObjectType || obj ) )
				menu.AddItem( new GUIContent( CONTEXT_COPY_LABEL ), false, CopyValue, property.Copy() );
			else
				menu.AddDisabledItem( new GUIContent( CONTEXT_COPY_LABEL ) );

			if( PasteBinWindow.ActiveClipboard == null || !property.CanPasteValue( PasteBinWindow.ActiveClipboard.RootValue, false ) )
				menu.AddDisabledItem( new GUIContent( CONTEXT_PASTE_LABEL ) );
			else
				menu.AddItem( new GUIContent( CONTEXT_PASTE_LABEL ), false, PasteValue, property.Copy() );

			menu.AddItem( new GUIContent( CONTEXT_PASTE_FROM_BIN_LABEL ), false, PasteValueFromBin, property.Copy() );
		}

		public static void OnObjectRightClicked( GenericMenu menu, Object obj )
		{
			AddInspectButtonToMenu( menu, obj, "" );
		}

		private static void AddInspectButtonToMenu( GenericMenu menu, Object obj, string path )
		{
			if( obj is Component )
			{
				string componentType = string.Concat( "/", obj.GetType().Name, " Component" );

				menu.AddItem( new GUIContent( string.Concat( NEW_TAB_LABEL, path, "/GameObject" ) ), false, () => InspectPlusWindow.Inspect( PreferablyGameObject( obj ), false ) );
				menu.AddItem( new GUIContent( string.Concat( NEW_TAB_LABEL, path, componentType ) ), false, () => InspectPlusWindow.Inspect( obj, false ) );
				menu.AddItem( new GUIContent( string.Concat( NEW_WINDOW_LABEL, path, "/GameObject" ) ), false, () => InspectPlusWindow.Inspect( PreferablyGameObject( obj ), true ) );
				menu.AddItem( new GUIContent( string.Concat( NEW_WINDOW_LABEL, path, componentType ) ), false, () => InspectPlusWindow.Inspect( obj, true ) );
			}
			else
			{
				menu.AddItem( new GUIContent( string.Concat( NEW_TAB_LABEL, path ) ), false, () => InspectPlusWindow.Inspect( obj, false ) );
				menu.AddItem( new GUIContent( string.Concat( NEW_WINDOW_LABEL, path ) ), false, () => InspectPlusWindow.Inspect( obj, true ) );
			}
		}
		#endregion

		private static void CopyValue( object obj )
		{
			PasteBinWindow.AddToClipboard( (SerializedProperty) obj );
		}

		private static void PasteValue( object obj )
		{
			if( PasteBinWindow.ActiveClipboard != null )
				( (SerializedProperty) obj ).PasteValue( PasteBinWindow.ActiveClipboard.RootValue );
		}

		private static void PasteValueFromBin( object obj )
		{
			PasteFromBin( obj, PasteBinContextWindow.PasteType.Normal );
		}

		private static void PasteComponentAsNewFromBin( object obj )
		{
			PasteFromBin( obj, PasteBinContextWindow.PasteType.ComponentAsNew );
		}

		private static void PasteFromBin( object obj, PasteBinContextWindow.PasteType pasteType )
		{
			if( obj is SerializedProperty || obj is Object[] )
			{
				PasteBinContextWindow window = ScriptableObject.CreateInstance<PasteBinContextWindow>();
				if( obj is SerializedProperty )
					window.Initialize( (SerializedProperty) obj );
				else
					window.Initialize( (Object[]) obj, pasteType );

				window.position = new Rect( new Vector2( -9999f, -9999f ), new Vector2( window.PreferredWidth, 9999f ) );
				window.ShowPopup();
				window.Focus();
			}
			else
				Debug.LogError( "Passed parameter is neither a SerializedProperty nor an Object." );
		}

		// If obj is Component, switches it to GameObject
		private static Object PreferablyGameObject( Object obj )
		{
			if( !obj )
				return null;

			if( obj is Component )
				return ( (Component) obj ).gameObject;

			return obj;
		}

		// If obj is Component, switches it to GameObject
		private static Object[] PreferablyGameObject( Object[] objs )
		{
			for( int i = 0; i < objs.Length; i++ )
				objs[i] = PreferablyGameObject( objs[i] );

			return objs;
		}
	}
}