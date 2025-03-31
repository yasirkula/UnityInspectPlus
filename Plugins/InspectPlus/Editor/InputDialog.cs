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
		private Action onCancel;

		private bool initialized;
		private Vector2 scrollPosition;

		protected void Initialize( string description, T value, Action<T> onResult, Action onCancel )
		{
			this.description = description;
			this.value = value;
			this.onResult = onResult;
			this.onCancel = onCancel;

			titleContent = GUIContent.none;
			minSize = new Vector2( 100f, 50f );

			ShowAuxWindow();
			Focus();
		}

		protected void OnDisable()
		{
			onCancel?.Invoke();
			onResult = null;
			onCancel = null;
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
				onResult?.Invoke( value );
				onResult = null;
				onCancel = null;

				Close();
			}

			if( !initialized )
			{
				GUILayout.EndVertical();

				float preferredHeight = GUILayoutUtility.GetLastRect().height;
				if( preferredHeight > 10f )
				{
					position = new Rect( position.position, new Vector2( position.width, preferredHeight + 15f ) );
					initialized = true;

					EditorGUI.FocusTextInControl( "InputD" );
					GUIUtility.ExitGUI();
				}
			}

			EditorGUILayout.EndScrollView();
		}

		protected abstract void OnInputGUI();
	}

	public class StringInputDialog : InputDialog<string>
	{
		public static void Show( string description, string value, Action<string> onResult, Action onCancel = null )
		{
			CreateInstance<StringInputDialog>().Initialize( description, value, onResult, onCancel );
		}

		protected override void OnInputGUI()
		{
			value = EditorGUILayout.TextField( GUIContent.none, value );
		}
	}
}