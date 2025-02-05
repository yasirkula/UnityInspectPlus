using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InspectPlusNamespace
{
	public abstract class InputDialog<T> : EditorWindow
	{
		private string description;
		protected T value;
		private Action<T> onResult;

		private bool initialized;
		private Vector2 scrollPosition;

		protected void Initialize( string description, T value, Action<T> onResult )
		{
			this.description = description;
			this.value = value;
			this.onResult = onResult;

			titleContent = GUIContent.none;
			minSize = new Vector2( 100f, 50f );
			position = new Rect( new Vector2( -9999f, -9999f ), new Vector2( 350f, 9999f ) );

			ShowAuxWindow();
			Focus();
		}

		protected void OnDisable()
		{
			SendResult( default( T ) );
		}

		private void OnGUI()
		{
			Event ev = Event.current;
			bool inputSubmitted = ev.type == EventType.KeyDown && ev.character == '\n';

			scrollPosition = EditorGUILayout.BeginScrollView( scrollPosition );

			if( !initialized )
				GUILayout.BeginVertical();

			GUILayout.Label( description, EditorStyles.wordWrappedLabel );

			GUI.SetNextControlName( "InputD" );
			OnInputGUI();

			if( GUILayout.Button( "OK" ) || inputSubmitted )
			{
				SendResult( value );
				Close();
			}

			if( !initialized )
			{
				GUILayout.EndVertical();

				float preferredHeight = GUILayoutUtility.GetLastRect().height;
				if( preferredHeight > 10f )
				{
					Vector2 size = new Vector2( position.width, preferredHeight + 15f );
					position = Utilities.GetScreenFittedRect( new Rect( GUIUtility.GUIToScreenPoint( ev.mousePosition ) - size * 0.5f, size ) );
					initialized = true;

					EditorGUI.FocusTextInControl( "InputD" );
					GUIUtility.ExitGUI();
				}
			}

			EditorGUILayout.EndScrollView();
		}

		protected abstract void OnInputGUI();

		private void SendResult( T value )
		{
			if( onResult != null )
			{
				onResult( value );
				onResult = null;
			}
		}
	}

	public class StringInputDialog : InputDialog<string>
	{
		public static void Show( string description, string value, Action<string> onResult )
		{
			CreateInstance<StringInputDialog>().Initialize( description, value, onResult );
		}

		protected override void OnInputGUI()
		{
			value = EditorGUILayout.TextField( GUIContent.none, value );
		}
	}
}