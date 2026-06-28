using System;
using System.Collections.Generic;
using System.Linq;
using FishingGame;
using Unity.Cinemachine;
using UnityEngine;

//! DOCUMENTATION: https://github.com/richardelms/FileBasedPlayerPrefs?tab=readme-ov-file#api

namespace FishingGame {
	public class PlayerPrefsHandler : MonoBehaviour {
		#region Fields

		public static  PlayerPrefsHandler Instance;
		private static DebugHandler       Debug;
		private readonly Dictionary<string, object> configurationSchema = new() {
			{
				"TargetFrameRate", 60
			}, {
				"DebugLevel", "Warning"
			}, {
				"LogFilter", ""
			}, {
				"MasterVolume", 0.5f
			}, {
				"InputSensitivity", 0.1f
			}, {
				"MusicVolume", 1f
			}, {
				"SfxVolume", 1f
			}, {
				"AmbienceVolume", 1f
			}, {
				"UIVolume", 1f
			}
		};

		private static CinemachineInputAxisController cmInputAxisController;

		#endregion

		#region Unity Functions

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void OnRuntimeInit() {
			Debug                 = new DebugHandler("PlayerPrefsHandler");
			cmInputAxisController = FindAnyObjectByType<CinemachineInputAxisController>();
		}

		private void Awake() {
			Debug ??= new DebugHandler("PlayerPrefsHandler");

			if (Instance == null) {
				Instance = this;
			} else {
				Destroy(gameObject);
				return;
			}

			var config = new FBPPConfig() {
				SaveFileName     = "preferences.cfg",
				AutoSaveData     = false,
				ScrambleSaveData = false,
				SaveFilePath     = Application.persistentDataPath,
				OnLoadError      = () => Debug.Log("Error loading FBPP data.", DebugLevel.Fatal)
			};

			FBPP.Start(config);

			CheckMissingKeysAndSave();

			LoadPreferences();
		}

		private void Start() {
			Debug.LogKv("DebugInformation:", DebugLevel.Info, new object[] {
				"isEditor", Application.isEditor,
				"isProduction", Application.version,
			});
		}

		#endregion

		#region Functions

		/// <summary>
		/// Save player preferences to file.
		/// </summary>
		public void SavePreferences() => FBPP.Save();


		/// <summary>
		/// Reset player preferences by deleting all saved data then saving.
		/// </summary>
		public void ResetPreferences() {
			FBPP.DeleteAll();
			CheckMissingKeysAndSave();
		}

		/// <summary>
		/// Update value without saving, requires saving afterward.
		/// </summary>
		/// <param name="busType">BusSlider.BusType</param>
		/// <param name="value">0-1 float</param>
		// public static void UpdateBusValue(BusSlider.BusType busType, float value) {
		// 	if (value is > 1 or < 0) {
		// 		Debug.LogError($"Invalid volume value: {value}. Must be between 0 and 1.");
		// 		return;
		// 	}
		//
		// 	AudioManager.Instance.SetBusVolume(busType, value);
		// 	FBPP.SetFloat(busType.ToString(), value);
		// }
		private void CheckMissingKeysAndSave() {
			Debug.Log("Checking for missing keys");
			foreach (var kvp in configurationSchema) {
				if (FBPP.HasKey(kvp.Key)) return;

				Debug.LogKv($"Key '{kvp.Key}' is missing, attempting to add now.",
				            DebugLevel.Warning, new object[] {
					            "Key", kvp.Key,
					            "DefaultValue", kvp.Value
				            });

				switch (kvp.Value) {
					case int val:
						FBPP.SetInt(kvp.Key, val);
						break;
					case string val:
						FBPP.SetString(kvp.Key, val);
						break;
					case bool val:
						FBPP.SetBool(kvp.Key, val);
						break;
					case float val:
						FBPP.SetFloat(kvp.Key, val);
						break;
				}
			}

			Debug.Log("Finished checking for missing keys, saving.");
			SavePreferences();
		}

		private void LoadPreferences() {
			//? Framerate Soft-cap/Target
			Preferences.Game.TargetFrameRate = FBPP.GetInt("TargetFrameRate", 60);
			Application.targetFrameRate      = Preferences.Game.TargetFrameRate;

			//? DebugHandler Level & Filter
			Preferences.DebugHandler.DbgLevel = FBPP.GetString("DebugLevel", "Error") switch {
				"None"    => DebugLevel.None,
				"Fatal"   => DebugLevel.Fatal,
				"Error"   => DebugLevel.Error,
				"Warning" => DebugLevel.Warning,
				"Info"    => DebugLevel.Info,
				"Debug"   => DebugLevel.Debug,
				_         => DebugLevel.Error
			};

			Preferences.DebugHandler.LogFilter = FBPP.GetString("LogFilter")
			                                         .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
			                                         .Select(s => s.Trim())
			                                         .ToList();

			//? Input
			Preferences.Input.MouseSensitivity              = FBPP.GetFloat("MouseSensitivity", 0.1f);
			cmInputAxisController.Controllers[0].Input.Gain = Preferences.Input.MouseSensitivity * 10;
			cmInputAxisController.Controllers[1].Input.Gain = Preferences.Input.MouseSensitivity * 10;
		}

		#endregion
	}
}