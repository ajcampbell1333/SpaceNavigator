#define DEBUG_LEVEL_LOG
#define DEBUG_LEVEL_WARN
#define DEBUG_LEVEL_ERROR

using System.Runtime.InteropServices;
using TDx.TDxInput;
using UnityEngine;
using UnityEditor;

public class SpaceNavigatorWindow : EditorWindow {
	// Device variables
	protected Sensor Sensor;
	protected Device Device;
	protected Keyboard Keyboard;

	// Rig components
	private GameObject _pivotGO, _cameraGO;
	private Transform _pivot, _camera;

	// Settings
	public float TranslationSensitivity { get { return _translationSensitivity * TranslationSensitivityScale; }	}
	private float _translationSensitivity;
	public float RotationSensitivity { get { return _rotationSensitivity * RotationSensitivityScale; } }
	private float _rotationSensitivity;
	public bool NavigationMode;
	
	// Setting defaults
	public const float TranslationSensitivityScale = 0.001f, RotationSensitivityScale = 0.05f;
	public const float TranslationSensitivityDefault = 1f, RotationSensitivityDefault = 1;
	public const int NavigationModeDefault = 1;

	// Setting storage keys
	private const string TransSensKey = "Translation sensitivity";
	private const string RotSensKey = "Rotation sensitivity";
	private const string ModeKey = "Navigation mode";

	/// <summary>
	/// Initializes the window.
	/// </summary>
	[MenuItem("Window/Space Navigator")]
	public static void Init() {
		SpaceNavigatorWindow window = GetWindow(typeof(SpaceNavigatorWindow)) as SpaceNavigatorWindow;

		if (window) {
			window.Show();
			window.ReadSettings();
			window.InitSpaceNavigator();
			window.InitCameraRig();
		}
	}
	/// <summary>
	/// Called when window is closed.
	/// </summary>
	public void OnDestroy() {
		WriteSettings();
		DisposeCameraRig();
		DisposeSpaceNavigator();
	}

	private void InitSpaceNavigator() {
		try {
			if (Device == null) {
				Device = new DeviceClass();
				Sensor = Device.Sensor;
				Keyboard = Device.Keyboard;
				Device.LoadPreferences("Unity");
			}
			if (!Device.IsConnected)
				Device.Connect();
		}
		catch (COMException ex) {
			D.error(ex.ToString());
		}
		D.log("Initialized");
	}

	public void DisposeSpaceNavigator() {
		try {
			if (Device != null && Device.IsConnected) {
				Device.Disconnect();

				D.log("Disconnected");
			}
		}
		catch (COMException ex) {
			D.error(ex.ToString());
		}
	}

	public Vector3 TranslationInWorldSpace {
		get {
			return (Sensor == null ?
				Vector3.zero :
				new Vector3(
					(float)Sensor.Translation.X,
					(float)Sensor.Translation.Y,
					-(float)Sensor.Translation.Z) *
					TranslationSensitivity);
		}
	}
	public Quaternion RotationInWorldSpace {
		get {
			return (Sensor == null ?
				Quaternion.identity :
				Quaternion.AngleAxis(
					(float)Sensor.Rotation.Angle * RotationSensitivity,
					new Vector3(
						-(float)Sensor.Rotation.X,
						-(float)Sensor.Rotation.Y,
						(float)Sensor.Rotation.Z)));
		}
	}
	public Quaternion RotationInLocalCoordSys(Transform coordSys) {
		return coordSys.rotation * RotationInWorldSpace * Quaternion.Inverse(coordSys.rotation);
	}

	/// <summary>
	/// Sets up a dummy camera rig like the scene camera.
	/// We can't move the camera, only the SceneView's pivot & rotation.
	/// For some reason the camera does not always have the same position offset to the pivot.
	/// This offset is unpredictable, so we have to update our dummy rig each time before using it.
	/// </summary>
	private void InitCameraRig() {
		// Create camera rig if one is not already present.
		if (!_pivotGO) {
			_cameraGO = new GameObject("Scene camera dummy") {hideFlags = HideFlags.HideAndDontSave};
			_camera = _cameraGO.transform;

			_pivotGO = new GameObject("Scene camera pivot dummy") {hideFlags = HideFlags.HideAndDontSave};
			_pivot = _pivotGO.transform;
			_pivot.parent = _camera;
		}

		SyncRigWithScene();
	}
	/// <summary>
	/// Position the dummy camera rig like the scene view camera.
	/// </summary>
	private void SyncRigWithScene() {
		if (SceneView.lastActiveSceneView) {
			_camera.position = SceneView.lastActiveSceneView.camera.transform.position;	// <- this value changes w.r.t. pivot !
			_camera.rotation = SceneView.lastActiveSceneView.camera.transform.rotation;
			_pivot.position = SceneView.lastActiveSceneView.pivot;
			_pivot.rotation = SceneView.lastActiveSceneView.rotation;
		}
	}
	private void DisposeCameraRig() {
		DestroyImmediate(_cameraGO);
		DestroyImmediate(_pivotGO);
	}

	/// <summary>
	/// This is called 100x per second (if the window content is visible).
	/// </summary>
	public void Update() {
		SceneView sceneView = SceneView.lastActiveSceneView;
		if (!sceneView) return;

		if (NavigationMode) {
			// Navigation mode.
			Navigate(sceneView);
		} else {
			// Manipulation mode.
			foreach (Transform transform in Selection.GetTransforms(SelectionMode.TopLevel | SelectionMode.Editable)) {
				// Translate the selected object in camera-space.
				Vector3 worldTranslation = sceneView.camera.transform.TransformPoint(TranslationInWorldSpace) -
											sceneView.camera.transform.position;
				if (worldTranslation != Vector3.zero)
					transform.Translate(worldTranslation, Space.World);

				// Rotate the selected object in camera-space.
				transform.rotation = RotationInLocalCoordSys(sceneView.camera.transform) * transform.rotation;
			}
		}

		//// Detect keyboard clicks (not working).
		//if (Keyboard.IsKeyDown(1))
		//	Debug.Log("Button 0 pressed");
		//if (Keyboard.IsKeyDown(2))
		//	Debug.Log("Button 1 pressed");
	}

	private void Navigate(SceneView sceneView) {
		if (TranslationInWorldSpace == Vector3.zero && RotationInWorldSpace == Quaternion.identity) return;

		SyncRigWithScene();

		_camera.Translate(TranslationInWorldSpace, Space.Self);

		//// Default rotation method, applies the whole quaternion to the camera.
		//Quaternion sceneCamera = sceneView.camera.transform.rotation;
		//Quaternion inputInWorldSpace = RotationInWorldSpace;
		//Quaternion inputInCameraSpace = sceneCamera * inputInWorldSpace * Quaternion.Inverse(sceneCamera);
		//_camera.rotation = inputInCameraSpace * _camera.rotation;

		// This method keeps the horizon horizontal at all times.
		// Perform azimuth in world coordinates.
		_camera.RotateAround(Vector3.up, RotationInWorldSpace.y);
		// Perform pitch in local coordinates.
		_camera.RotateAround(_camera.right, RotationInWorldSpace.x);

		// Update sceneview pivot and repaint view.
		sceneView.pivot = _pivot.position;
		sceneView.rotation = _pivot.rotation;
		sceneView.Repaint();
	}

	/// <summary>
	/// Draws the EditorWindow's GUI.
	/// </summary>
	public void OnGUI() {
		GUILayout.BeginVertical();

		NavigationMode = GUILayout.Toggle(NavigationMode, "Operate in navigation mode");

		SceneView sceneView = SceneView.lastActiveSceneView;
		if (GUILayout.Button("Reset")) {
			if (sceneView) {
				sceneView.pivot = new Vector3(0, 0, 0);
				sceneView.rotation = Quaternion.identity;
				sceneView.Repaint();
			}
		}
		if (sceneView && _pivot) {
			GUILayout.Label(string.Format("Scene cam pos\t{0}", SceneView.lastActiveSceneView.camera.transform.position));
			GUILayout.Label(string.Format("Scene pivot dummy lpos\t{0}", _pivot.localPosition));
			GUILayout.Label(string.Format("Scene pivot pos\t{0}", SceneView.lastActiveSceneView.pivot));
		} else {
			GUILayout.Label(string.Format("Scene cam pos\t{0}", Vector3.zero.ToString()));
			GUILayout.Label(string.Format("Scene pivot dummy lpos\t{0}", Vector3.zero.ToString()));
			GUILayout.Label(string.Format("Scene pivot pos\t{0}", Vector3.zero.ToString()));
		}

		if (GUILayout.Button("Reset sensitivity")) {
			_translationSensitivity = 1;
			_rotationSensitivity = 1;
		}
		GUILayout.BeginHorizontal();
		GUILayout.Label(string.Format("T Sens {0:0.00000}", _translationSensitivity));
		_translationSensitivity = GUILayout.HorizontalSlider(_translationSensitivity, 0.001f, 5f);
		GUILayout.EndHorizontal();

		GUILayout.BeginHorizontal();
		GUILayout.Label(string.Format("R Sens {0:0.00000}", _rotationSensitivity));
		_rotationSensitivity = GUILayout.HorizontalSlider(_rotationSensitivity, 0.001f, 5f);
		GUILayout.EndHorizontal();

		GUILayout.EndVertical();
	}

	/// <summary>
	/// Reads the settings.
	/// </summary>
	private void ReadSettings() {
		_translationSensitivity = EditorPrefs.GetFloat(TransSensKey, TranslationSensitivityDefault);
		_rotationSensitivity = EditorPrefs.GetFloat(RotSensKey, RotationSensitivityDefault);
		NavigationMode = EditorPrefs.GetInt(ModeKey, NavigationModeDefault) == 1;
	}
	/// <summary>
	/// Writes the settings.
	/// </summary>
	private void WriteSettings() {
		EditorPrefs.SetFloat(TransSensKey, _translationSensitivity);
		EditorPrefs.SetFloat(RotSensKey, _rotationSensitivity);
		EditorPrefs.SetInt(ModeKey, NavigationMode ? 1 : 0);
	}
}