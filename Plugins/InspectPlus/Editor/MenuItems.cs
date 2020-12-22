using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using AssetFilesClipboard = InspectPlusNamespace.SerializablePropertyExtensions.AssetFilesClipboard;
using GameObjectHierarchyClipboard = InspectPlusNamespace.SerializablePropertyExtensions.GameObjectHierarchyClipboard;

namespace InspectPlusNamespace
{
	internal static class MenuItems
	{
		internal const string NEW_TAB_LABEL = "Open In New Tab";
		internal const string NEW_WINDOW_LABEL = "Open In New Window";

		private const string CONTEXT_COPY_LABEL = "Copy (Inspect+)";
		private const string CONTEXT_COPY_COMPONENT_LABEL = "Copy Component (Inspect+)";
		private const string CONTEXT_COPY_REFERENCE_LABEL = "Copy/Reference";
		private const string CONTEXT_COPY_ASSET_FILES_LABEL = "Copy/Asset File(s)";
		private const string CONTEXT_COPY_COMPLETE_GAMEOBJECT_WITHOUT_CHILDREN_LABEL = "Copy/Complete GameObject (This Object Only)";
		private const string CONTEXT_COPY_COMPLETE_GAMEOBJECT_WITH_CHILDREN_LABEL = "Copy/Complete GameObject (Include Children)";
		private const string CONTEXT_PASTE_COMPLETE_GAMEOBJECT_LABEL = "Paste/Complete GameObject";
		private const string CONTEXT_PASTE_COMPLETE_GAMEOBJECT_FROM_BIN_LABEL = "Paste/Complete GameObject From Bin";
		private const string CONTEXT_PASTE_ASSET_FILES_LABEL = "Paste/Asset File(s)";
		private const string CONTEXT_PASTE_ASSET_FILES_FROM_BIN_LABEL = "Paste/Asset File(s) From Bin";
		private const string CONTEXT_PASTE_LABEL = "Paste (Inspect+)";
		private const string CONTEXT_PASTE_VALUES_LABEL = "Paste Values (Inspect+)";
		private const string CONTEXT_PASTE_COMPONENT_VALUES_LABEL = "Paste Component Values (Inspect+)";
		private const string CONTEXT_PASTE_COMPONENT_AS_NEW_LABEL = "Paste Component As New (Inspect+)";
		private const string CONTEXT_PASTE_FROM_BIN_LABEL = "Paste From Bin (Inspect+)";
		private const string CONTEXT_PASTE_VALUES_FROM_BIN_LABEL = "Paste Values From Bin (Inspect+)";
		private const string CONTEXT_PASTE_COMPONENT_VALUES_FROM_BIN_LABEL = "Paste Component Values From Bin (Inspect+)";
		private const string CONTEXT_PASTE_COMPONENT_AS_NEW_FROM_BIN_LABEL = "Paste Component As New From Bin (Inspect+)";

		private static List<Object> objectsToOpenPasteBinWith;

		#region New Tab/Window Buttons
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

		[MenuItem( "GameObject/Inspect+/" + NEW_TAB_LABEL, validate = true )]
		[MenuItem( "GameObject/Inspect+/" + NEW_WINDOW_LABEL, validate = true )]
		[MenuItem( "Assets/Inspect+/" + NEW_TAB_LABEL, validate = true )]
		[MenuItem( "Assets/Inspect+/" + NEW_WINDOW_LABEL, validate = true )]
		private static bool GameObjectMenuValidate( MenuCommand command )
		{
			return Selection.objects.Length > 0;
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
		#endregion

		#region Copy/Paste Buttons
		[MenuItem( "GameObject/Inspect+/" + CONTEXT_COPY_REFERENCE_LABEL, priority = 50 )]
		[MenuItem( "Assets/Inspect+/" + CONTEXT_COPY_REFERENCE_LABEL, priority = 1501 )]
		[MenuItem( "CONTEXT/Component/" + CONTEXT_COPY_COMPONENT_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/ScriptableObject/" + CONTEXT_COPY_LABEL, priority = 1450 )]
		[MenuItem( "CONTEXT/Material/" + CONTEXT_COPY_LABEL, priority = 1450 )]
		private static void ContextMenuItemCopyObject( MenuCommand command )
		{
			if( command.context )
				PasteBinWindow.AddToClipboard( command.context, Utilities.GetDetailedObjectName( command.context ), command.context );
			else
				PasteBinWindow.AddToClipboard( Selection.activeObject, Utilities.GetDetailedObjectName( Selection.activeObject ), Selection.activeObject );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_COMPONENT_AS_NEW_LABEL, priority = 1450 )]
		private static void ContextMenuItemPasteComponentAsNew( MenuCommand command )
		{
			if( PasteBinWindow.ActiveClipboard != null )
				PasteBinWindow.ActiveClipboard.PasteAsNewComponent( command.context as Component );
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

				EditorApplication.update -= CallPasteComponentAsNewFromBinOnce;
				EditorApplication.update += CallPasteComponentAsNewFromBinOnce;
			}
			else if( objectsToOpenPasteBinWith != null )
			{
				PasteComponentAsNewFromBin( objectsToOpenPasteBinWith.ToArray() );
				objectsToOpenPasteBinWith = null;
			}
		}

		private static void CallPasteComponentAsNewFromBinOnce()
		{
			EditorApplication.update -= CallPasteComponentAsNewFromBinOnce;
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
			return ValidatePasteOperation( command ) && PasteBinWindow.ActiveClipboard != null && PasteBinWindow.ActiveClipboard.CanPasteAsNewComponent( command.context as Component );
		}

		[MenuItem( "CONTEXT/Component/" + CONTEXT_PASTE_COMPONENT_AS_NEW_FROM_BIN_LABEL, validate = true )]
		private static bool ContextMenuItemPasteComponentAsNewFromBinValidate( MenuCommand command )
		{
			return ValidatePasteOperation( command );
		}

		[MenuItem( "GameObject/Inspect+/" + CONTEXT_COPY_REFERENCE_LABEL, validate = true )]
		[MenuItem( "Assets/Inspect+/" + CONTEXT_COPY_REFERENCE_LABEL, validate = true )]
		private static bool ContextMenuItemCopyObjectValidate( MenuCommand command )
		{
			return Selection.activeObject;
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
		#endregion

		#region Complete GameObject Copy/Paste Buttons
		[MenuItem( "GameObject/Inspect+/" + CONTEXT_COPY_COMPLETE_GAMEOBJECT_WITHOUT_CHILDREN_LABEL, priority = 50 )]
		private static void MenuItemCopyCompleteGameObjectWithoutChildren( MenuCommand command )
		{
			// We are using EditorApplication.update to copy all selected GameObjects in one batch (else-clause)
			if( command.context )
			{
				EditorApplication.update -= CallCopyCompleteGameObjectWithoutChildrenOnce;
				EditorApplication.update += CallCopyCompleteGameObjectWithoutChildrenOnce;
			}
			else
				CopyCompleteGameObject( false );
		}

		[MenuItem( "GameObject/Inspect+/" + CONTEXT_COPY_COMPLETE_GAMEOBJECT_WITH_CHILDREN_LABEL, priority = 50 )]
		private static void MenuItemCopyCompleteGameObjectWithChildren( MenuCommand command )
		{
			// We are using EditorApplication.update to copy all selected GameObjects in one batch (else-clause)
			if( command.context )
			{
				EditorApplication.update -= CallCopyCompleteGameObjectWithChildrenOnce;
				EditorApplication.update += CallCopyCompleteGameObjectWithChildrenOnce;
			}
			else
				CopyCompleteGameObject( true );
		}

		private static void CopyCompleteGameObject( bool withChildren )
		{
			GameObject[] selectedGameObjects = Selection.GetFiltered<GameObject>( SelectionMode.TopLevel | SelectionMode.ExcludePrefab | SelectionMode.Editable );

			// Sorting selection can be important when all copied objects have the same name because their order will matter for Smart Paste's RelativePath
			System.Array.Sort( selectedGameObjects, ( go1, go2 ) => CompareHierarchySiblingIndices( go1.transform, go2.transform ) );

			if( selectedGameObjects.Length > 0 )
			{
				string label = Utilities.GetDetailedObjectName( selectedGameObjects[0] );
				if( selectedGameObjects.Length > 1 )
					label += " (and " + ( selectedGameObjects.Length - 1 ) + " more)";

				PasteBinWindow.AddToClipboard( new GameObjectHierarchyClipboard( selectedGameObjects, withChildren ), label + " (Complete GameObject)", null );
			}
		}

		[MenuItem( "GameObject/Inspect+/" + CONTEXT_PASTE_COMPLETE_GAMEOBJECT_LABEL, priority = 50 )]
		private static void MenuItemPasteCompleteGameObject( MenuCommand command )
		{
			GameObject gameObject = PreferablyGameObject( command.context ) as GameObject;
			if( PasteBinWindow.ActiveClipboard != null && PasteBinWindow.ActiveClipboard.CanPasteCompleteGameObject( gameObject ) )
				PasteBinWindow.ActiveClipboard.PasteCompleteGameObject( gameObject, true );
		}

		[MenuItem( "GameObject/Inspect+/" + CONTEXT_PASTE_COMPLETE_GAMEOBJECT_FROM_BIN_LABEL, priority = 50 )]
		private static void MenuItemPasteCompleteGameObjectFromBin( MenuCommand command )
		{
			// See ContextMenuItemPasteObjectFromBin for the purpose of EditorApplication.update here
			if( command.context )
			{
				GameObject gameObject = PreferablyGameObject( command.context ) as GameObject;
				if( !gameObject || AssetDatabase.Contains( gameObject ) )
					return;

				if( objectsToOpenPasteBinWith == null )
					objectsToOpenPasteBinWith = new List<Object>( 2 ) { command.context };
				else
					objectsToOpenPasteBinWith.Add( command.context );

				EditorApplication.update -= CallPasteCompleteGameObjectFromBinOnce;
				EditorApplication.update += CallPasteCompleteGameObjectFromBinOnce;
			}
			else if( objectsToOpenPasteBinWith != null )
			{
				PasteGameObjectHierarchyFromBin( objectsToOpenPasteBinWith.ToArray() );
				objectsToOpenPasteBinWith = null;
			}
			else
				PasteGameObjectHierarchyFromBin( new Object[1] { null } );
		}

		private static void CallCopyCompleteGameObjectWithoutChildrenOnce()
		{
			EditorApplication.update -= CallCopyCompleteGameObjectWithoutChildrenOnce;
			MenuItemCopyCompleteGameObjectWithoutChildren( new MenuCommand( null ) );
		}

		private static void CallCopyCompleteGameObjectWithChildrenOnce()
		{
			EditorApplication.update -= CallCopyCompleteGameObjectWithChildrenOnce;
			MenuItemCopyCompleteGameObjectWithChildren( new MenuCommand( null ) );
		}

		private static void CallPasteCompleteGameObjectFromBinOnce()
		{
			EditorApplication.update -= CallPasteCompleteGameObjectFromBinOnce;
			MenuItemPasteCompleteGameObjectFromBin( new MenuCommand( null ) );
		}

		[MenuItem( "GameObject/Inspect+/" + CONTEXT_COPY_COMPLETE_GAMEOBJECT_WITHOUT_CHILDREN_LABEL, validate = true )]
		[MenuItem( "GameObject/Inspect+/" + CONTEXT_COPY_COMPLETE_GAMEOBJECT_WITH_CHILDREN_LABEL, validate = true )]
		private static bool MenuItemCopyCompleteGameObjectValidate( MenuCommand command )
		{
			return Selection.GetFiltered<GameObject>( SelectionMode.TopLevel | SelectionMode.ExcludePrefab | SelectionMode.Editable ).Length > 0;
		}

		[MenuItem( "GameObject/Inspect+/" + CONTEXT_PASTE_COMPLETE_GAMEOBJECT_LABEL, validate = true )]
		private static bool MenuItemPasteCompleteGameObjectValidate( MenuCommand command )
		{
			return PasteBinWindow.ActiveClipboard != null && PasteBinWindow.ActiveClipboard.CanPasteCompleteGameObject( PreferablyGameObject( command.context ) as GameObject );
		}

		[MenuItem( "GameObject/Inspect+/" + CONTEXT_PASTE_COMPLETE_GAMEOBJECT_FROM_BIN_LABEL, validate = true )]
		private static bool MenuItemPasteCompleteGameObjectFromBinValidate( MenuCommand command )
		{
			return !( command.context as GameObject ) || !AssetDatabase.Contains( command.context );
		}
		#endregion

		#region Asset File Copy/Paste Buttons
		[MenuItem( "Assets/Inspect+/" + CONTEXT_COPY_ASSET_FILES_LABEL, priority = 1501 )]
		private static void MenuItemCopyAssetFiles( MenuCommand command )
		{
			// We are using EditorApplication.update to copy all selected asset files in one batch (else-clause)
			if( command.context )
			{
				EditorApplication.update -= CallCopyAssetFilesOnce;
				EditorApplication.update += CallCopyAssetFilesOnce;
			}
			else
			{
				string[] selectedAssets = GetSelectedAssetPaths( false, true );
				if( selectedAssets.Length > 0 )
				{
					string label = selectedAssets[0];
					if( selectedAssets.Length > 1 )
						label += " (and " + ( selectedAssets.Length - 1 ) + " more)";

					PasteBinWindow.AddToClipboard( new AssetFilesClipboard( selectedAssets ), label + " (Asset File)", null );
				}
			}
		}

		[MenuItem( "Assets/Inspect+/" + CONTEXT_PASTE_ASSET_FILES_LABEL, priority = 1501 )]
		private static void MenuItemPasteAssetFiles( MenuCommand command )
		{
			// We are using EditorApplication.update to paste files to all target paths in one batch (else-clause)
			if( command.context )
			{
				EditorApplication.update -= CallPasteAssetFilesOnce;
				EditorApplication.update += CallPasteAssetFilesOnce;
			}
			else
			{
				string[] selectedAssets = GetSelectedAssetPaths( true, false );
				if( selectedAssets.Length > 0 && PasteBinWindow.ActiveClipboard != null && PasteBinWindow.ActiveClipboard.CanPasteAssetFiles( selectedAssets ) )
					PasteBinWindow.ActiveClipboard.PasteAssetFiles( selectedAssets );
			}
		}

		[MenuItem( "Assets/Inspect+/" + CONTEXT_PASTE_ASSET_FILES_FROM_BIN_LABEL, priority = 1501 )]
		private static void MenuItemPasteAssetFilesFromBin( MenuCommand command )
		{
			// See ContextMenuItemPasteObjectFromBin for the purpose of EditorApplication.update here
			if( command.context )
			{
				EditorApplication.update -= CallPasteAssetFilesFromBinOnce;
				EditorApplication.update += CallPasteAssetFilesFromBinOnce;
			}
			else
			{
				string[] selectedAssets = GetSelectedAssetPaths( true, false );
				if( selectedAssets.Length > 0 )
					PasteAssetFilesFromBin( selectedAssets );
			}
		}

		private static void CallCopyAssetFilesOnce()
		{
			EditorApplication.update -= CallCopyAssetFilesOnce;
			MenuItemCopyAssetFiles( new MenuCommand( null ) );
		}

		private static void CallPasteAssetFilesOnce()
		{
			EditorApplication.update -= CallPasteAssetFilesOnce;
			MenuItemPasteAssetFiles( new MenuCommand( null ) );
		}

		private static void CallPasteAssetFilesFromBinOnce()
		{
			EditorApplication.update -= CallPasteAssetFilesFromBinOnce;
			MenuItemPasteAssetFilesFromBin( new MenuCommand( null ) );
		}

		[MenuItem( "Assets/Inspect+/" + CONTEXT_COPY_ASSET_FILES_LABEL, validate = true )]
		private static bool MenuItemCopyAssetFilesValidate( MenuCommand command )
		{
			return GetSelectedAssetPaths( false, true ).Length > 0;
		}

		[MenuItem( "Assets/Inspect+/" + CONTEXT_PASTE_ASSET_FILES_LABEL, validate = true )]
		private static bool MenuItemPasteAssetFilesValidate( MenuCommand command )
		{
			return PasteBinWindow.ActiveClipboard != null && PasteBinWindow.ActiveClipboard.CanPasteAssetFiles( GetSelectedAssetPaths( true, false ) );
		}

		[MenuItem( "Assets/Inspect+/" + CONTEXT_PASTE_ASSET_FILES_FROM_BIN_LABEL, validate = true )]
		private static bool MenuItemPasteAssetFilesFromBinValidate( MenuCommand command )
		{
			return GetSelectedAssetPaths( true, false ).Length > 0;
		}
		#endregion

		#region Other Context Menu Buttons
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

		#region Helper Functions
		private static void CopyValue( object obj )
		{
			PasteBinWindow.AddToClipboard( (SerializedProperty) obj );
		}

		private static void PasteValue( object obj )
		{
			if( PasteBinWindow.ActiveClipboard != null )
				( (SerializedProperty) obj ).PasteValue( PasteBinWindow.ActiveClipboard );
		}

		private static void PasteValueFromBin( object obj )
		{
			PasteFromBin( obj, PasteBinContextWindow.PasteType.Normal );
		}

		private static void PasteComponentAsNewFromBin( object obj )
		{
			PasteFromBin( obj, PasteBinContextWindow.PasteType.ComponentAsNew );
		}

		private static void PasteGameObjectHierarchyFromBin( object obj )
		{
			PasteFromBin( obj, PasteBinContextWindow.PasteType.CompleteGameObject );
		}

		private static void PasteAssetFilesFromBin( object obj )
		{
			string[] assetPaths = (string[]) obj;
			Utilities.ConvertAbsolutePathsToRelativePaths( assetPaths );

			Object[] parentFolders = new Object[assetPaths.Length];
			for( int i = 0; i < assetPaths.Length; i++ )
				parentFolders[i] = AssetDatabase.LoadMainAssetAtPath( assetPaths[i] );

			PasteFromBin( parentFolders, PasteBinContextWindow.PasteType.AssetFiles );
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

		// Returns -1 if t1 is above t2 in Hierarchy, 1 if t1 is below t2 in Hierarchy and 0 if they are the same object
		private static int CompareHierarchySiblingIndices( Transform t1, Transform t2 )
		{
			Transform parent1 = t1.parent;
			Transform parent2 = t2.parent;

			if( parent1 == parent2 )
				return t1.GetSiblingIndex() - t2.GetSiblingIndex();

			int deltaHierarchyDepth = 0;
			while( parent1 )
			{
				deltaHierarchyDepth++;
				parent1 = parent1.parent;
			}
			while( parent2 )
			{
				deltaHierarchyDepth--;
				parent2 = parent2.parent;
			}

			for( ; deltaHierarchyDepth > 0; deltaHierarchyDepth-- )
			{
				t1 = t1.parent;
				if( t1 == t2 )
					return 1;
			}
			for( ; deltaHierarchyDepth < 0; deltaHierarchyDepth++ )
			{
				t2 = t2.parent;
				if( t1 == t2 )
					return -1;
			}

			while( t1.parent != t2.parent )
			{
				t1 = t1.parent;
				t2 = t2.parent;
			}

			return t1.GetSiblingIndex() - t2.GetSiblingIndex();
		}

		// Returns selected assets' paths
		private static string[] GetSelectedAssetPaths( bool convertFilesToFolders, bool removeChildPaths )
		{
			// Calculate all selected assets' paths
			List<string> selection = new List<string>( Selection.assetGUIDs );
			for( int i = selection.Count - 1; i >= 0; i-- )
			{
				if( !string.IsNullOrEmpty( selection[i] ) )
					selection[i] = AssetDatabase.GUIDToAssetPath( selection[i] );

				if( string.IsNullOrEmpty( selection[i] ) )
				{
					selection.RemoveAt( i );
					continue;
				}
			}

			if( convertFilesToFolders )
			{
				// For files, change the path to the containing folder's path
				for( int i = selection.Count - 1; i >= 0; i-- )
				{
					if( File.Exists( selection[i] ) )
						selection[i] = Path.GetDirectoryName( selection[i] );
				}
			}

			if( removeChildPaths )
			{
				// Remove redundant paths (e.g. if both a folder and one of its files are selected, omit the file's path)
				for( int i = selection.Count - 1; i >= 0; i-- )
				{
					for( int j = i - 1; j >= 0; j-- )
					{
						if( selection[i].Length < selection[j].Length )
						{
							if( selection[j].StartsWith( selection[i] + "/" ) )
							{
								selection.RemoveAt( j );
								i--;
							}
						}
						else if( selection[i].StartsWith( selection[j] + "/" ) )
						{
							selection.RemoveAt( i );
							break;
						}
					}
				}
			}

			// Convert relative paths to absolute paths
			for( int i = selection.Count - 1; i >= 0; i-- )
			{
				try
				{
					selection[i] = Path.GetFullPath( selection[i] );
					if( string.IsNullOrEmpty( selection[i] ) )
						selection.RemoveAt( i );
				}
				catch( System.Exception e )
				{
					Debug.LogException( e );
					selection.RemoveAt( i );
				}
			}

			// Remove duplicate paths
			for( int i = selection.Count - 1; i >= 0; i-- )
			{
				for( int j = i - 1; j >= 0; j-- )
				{
					if( selection[i] == selection[j] )
					{
						selection.RemoveAt( i );
						break;
					}
				}
			}

			return selection.ToArray();
		}
		#endregion
	}
}