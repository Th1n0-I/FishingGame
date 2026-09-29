using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// Weather that changes by itself (or on keys 3 to 8) and everything that comes with it: cloud cover, storm towers,
// wind and gusts, rain, lightning and how dark the day gets. NoiseController reads Current every frame and creates one
// of these when the scene has none.
public class WeatherSystem : MonoBehaviour {
	public enum WeatherType { Clear, Fair, Cloudy, Overcast, Rain, Storm }

	[System.Serializable]
	public struct WeatherValues {
		[Range(0, 1), Tooltip("Cloud coverage, 0.78 is the fair weather look.")]
		public float coverage;
		[Tooltip("Multiplies the cloud density.")]
		public float density;
		[Range(0, 1), Tooltip("How tall the active storm cells grow, 1 = cumulonimbus with an anvil.")]
		public float towers;
		[Range(0, 1), Tooltip("Share of the storm cells that are active.")]
		public float stormCells;
		[Range(0, 1)]
		public float cirrus;
		[Tooltip("Wind speed in m/s.")]
		public float windSpeed;
		[Range(0, 1), Tooltip("How much the wind speed varies.")]
		public float gusts;
		[Range(0, 1)]
		public float rain;
		[Tooltip("Lightning strikes per minute.")]
		public float lightning;
		[Range(0, 1), Tooltip("How much the sun, the sky and the ambient light dim.")]
		public float darkness;
		[Tooltip("How far you can see compared to fair weather.")]
		public float visibility;

		public static WeatherValues Lerp(WeatherValues a, WeatherValues b, float t) => new WeatherValues {
			coverage   = Mathf.Lerp(a.coverage,   b.coverage,   t),
			density    = Mathf.Lerp(a.density,    b.density,    t),
			towers     = Mathf.Lerp(a.towers,     b.towers,     t),
			stormCells = Mathf.Lerp(a.stormCells, b.stormCells, t),
			cirrus     = Mathf.Lerp(a.cirrus,     b.cirrus,     t),
			windSpeed  = Mathf.Lerp(a.windSpeed,  b.windSpeed,  t),
			gusts      = Mathf.Lerp(a.gusts,      b.gusts,      t),
			rain       = Mathf.Lerp(a.rain,       b.rain,       t),
			lightning  = Mathf.Lerp(a.lightning,  b.lightning,  t),
			darkness   = Mathf.Lerp(a.darkness,   b.darkness,   t),
			visibility = Mathf.Lerp(a.visibility, b.visibility, t),
		};
	}

	[Header("Weather")]
	[SerializeField] private WeatherType weather = WeatherType.Fair;
	[SerializeField, Tooltip("Changes the weather by itself now and then.")]
	private bool autoWeather = true;
	[SerializeField, Tooltip("Real minutes between changes in auto mode (random between the two).")]
	private Vector2 changeMinutes = new Vector2(2f, 4f);
	[SerializeField, Tooltip("Seconds a change takes.")]
	private float transitionSeconds = 40f;

	[Header("States")]
	[SerializeField] private WeatherValues clear    = Values(0.62f, 0.90f, 0.00f, 0.00f, 0.45f, 30f, 0.15f, 0.00f, 0f,  0.00f, 1.25f);
	[SerializeField] private WeatherValues fair     = Values(0.78f, 1.00f, 0.00f, 0.05f, 0.65f, 40f, 0.20f, 0.00f, 0f,  0.00f, 1.00f);
	[SerializeField] private WeatherValues cloudy   = Values(0.88f, 1.10f, 0.35f, 0.25f, 0.55f, 45f, 0.30f, 0.00f, 0f,  0.15f, 0.80f);
	[SerializeField] private WeatherValues overcast = Values(0.97f, 1.15f, 0.00f, 0.10f, 0.25f, 45f, 0.30f, 0.15f, 0f,  0.45f, 0.50f);
	[SerializeField] private WeatherValues rainy    = Values(0.95f, 1.35f, 0.40f, 0.30f, 0.15f, 55f, 0.45f, 0.70f, 0.5f, 0.60f, 0.35f);
	[SerializeField] private WeatherValues storm    = Values(0.88f, 1.50f, 1.00f, 0.55f, 0.30f, 70f, 0.70f, 1.00f, 10f, 0.75f, 0.30f);

	public WeatherType   Weather     => weather;
	public bool          AutoWeather => autoWeather;
	// The weather right now, in between two states while changing.
	public WeatherValues Current     { get; private set; }
	// Wind speed with the gusts, m/s.
	public float         WindSpeed   { get; private set; }

	private WeatherValues previous;
	private float         blend = 1, nextChange, messageUntil, gustTime;
	private DayNightCycle dayNightCycle;
	private Light[]       cycleLights = new Light[0];
	private Material      skyMaterial, originalSky;
	private float         skyExposure = 1.3f;

	private static readonly int SkyExposure = Shader.PropertyToID("_Exposure");

	private static WeatherValues Values(float coverage, float density, float towers, float stormCells, float cirrus, float windSpeed,
	                                    float gusts, float rain, float lightning, float darkness, float visibility) => new WeatherValues {
		coverage = coverage, density = density, towers = towers, stormCells = stormCells, cirrus = cirrus, windSpeed = windSpeed,
		gusts = gusts, rain = rain, lightning = lightning, darkness = darkness, visibility = visibility
	};

	private WeatherValues Target(WeatherType type) => type switch {
		WeatherType.Clear    => clear,
		WeatherType.Cloudy   => cloudy,
		WeatherType.Overcast => overcast,
		WeatherType.Rain     => rainy,
		WeatherType.Storm    => storm,
		_                    => fair
	};

	private void Awake() {
		Current    = Target(weather);
		previous   = Current;
		WindSpeed  = Current.windSpeed;
		nextChange = UnityEngine.Time.time + NextChangeDelay();

		// A copy of the skybox, so it can get darker without touching the asset.
		originalSky = RenderSettings.skybox;
		if (originalSky) {
			skyMaterial = new Material(originalSky);
			RenderSettings.skybox = skyMaterial;
			if (skyMaterial.HasProperty(SkyExposure)) skyExposure = skyMaterial.GetFloat(SkyExposure);
		}
	}

	private void Start() {
		// Sets the lights every frame, so dimming them after it (in LateUpdate) doesn't add up over frames.
		dayNightCycle = FindAnyObjectByType<DayNightCycle>();
		if (dayNightCycle) cycleLights = dayNightCycle.GetComponentsInChildren<Light>(true);
	}

	private void OnDestroy() {
		if (skyMaterial) {
			if (RenderSettings.skybox == skyMaterial) RenderSettings.skybox = originalSky;
			Destroy(skyMaterial);
		}
	}

	public void SetWeather(WeatherType type) {
		previous = Current;
		weather  = type;
		blend    = 0;
	}

	private float NextChangeDelay() => Random.Range(changeMinutes.x, Mathf.Max(changeMinutes.x, changeMinutes.y)) * 60f;

	private void Update() {
		CheckKeys();

		// Auto mode walks to a neighbouring state most of the time, so storms build up through clouds and rain.
		if (autoWeather && UnityEngine.Time.time >= nextChange) {
			int step = Random.value < 0.8f ? (Random.value < 0.5f ? -1 : 1) : Random.Range(-2, 3);
			SetWeather((WeatherType)Mathf.Clamp((int)weather + step, 0, (int)WeatherType.Storm));
			nextChange = UnityEngine.Time.time + NextChangeDelay();
		}

		blend   = Mathf.Min(1, blend + UnityEngine.Time.deltaTime / Mathf.Max(transitionSeconds, 0.01f));
		Current = WeatherValues.Lerp(previous, Target(weather), Mathf.SmoothStep(0, 1, blend));

		// Gusts: slow noise on top of the wind, a bit faster and stronger in storms.
		gustTime += UnityEngine.Time.deltaTime * (0.05f + 0.1f * Current.gusts);
		float gust = Mathf.PerlinNoise(gustTime, 0.37f) * 2f - 1f;
		WindSpeed = Current.windSpeed * Mathf.Max(0.2f, 1f + gust * Current.gusts * 0.6f);
	}

	private void CheckKeys() {
		var keyboard = Keyboard.current;
		if (keyboard == null) return;

		bool pressed = true;
		if (keyboard.digit3Key.wasPressedThisFrame) SetWeather(WeatherType.Clear);
		else if (keyboard.digit4Key.wasPressedThisFrame) SetWeather(WeatherType.Fair);
		else if (keyboard.digit5Key.wasPressedThisFrame) SetWeather(WeatherType.Cloudy);
		else if (keyboard.digit6Key.wasPressedThisFrame) SetWeather(WeatherType.Overcast);
		else if (keyboard.digit7Key.wasPressedThisFrame) SetWeather(WeatherType.Rain);
		else if (keyboard.digit8Key.wasPressedThisFrame) SetWeather(WeatherType.Storm);
		else if (keyboard.digit9Key.wasPressedThisFrame) {
			autoWeather = !autoWeather;
			nextChange  = UnityEngine.Time.time + NextChangeDelay();
		}
		else pressed = false;
		if (pressed) messageUntil = UnityEngine.Time.unscaledTime + 3;
	}

	// After DayNightCycle has set the lights for this frame.
	private void LateUpdate() {
		float light = 1f - 0.7f * Current.darkness;
		float sky   = 1f - 0.5f * Current.darkness;

		if (skyMaterial && skyMaterial.HasProperty(SkyExposure)) skyMaterial.SetFloat(SkyExposure, skyExposure * (1f - 0.6f * Current.darkness));
		if (!dayNightCycle) return;

		foreach (var sceneLight in cycleLights) if (sceneLight) sceneLight.intensity *= light;
		if (RenderSettings.sun && !RenderSettings.sun.transform.IsChildOf(dayNightCycle.transform)) RenderSettings.sun.intensity *= light;
		if (dayNightCycle.ControlsSceneAmbient && RenderSettings.ambientMode == AmbientMode.Flat) {
			// Greyer and darker, overcast light has no colour to it.
			Color ambient = RenderSettings.ambientLight;
			float grey    = ambient.grayscale;
			ambient = Color.Lerp(ambient, new Color(grey, grey, grey), 0.6f * Current.darkness) * sky;
			RenderSettings.ambientLight = ambient;
			var probe = new SphericalHarmonicsL2();
			probe.AddAmbientLight(ambient);
			RenderSettings.ambientProbe = probe;
		}
	}

	private void OnGUI() {
		if (UnityEngine.Time.unscaledTime > messageUntil) return;
		GUI.Label(new Rect(10, Screen.height - 105, 1000, 25),
		          $"Weather: {weather}{(blend < 1 ? " (changing)" : "")}    [3] Clear [4] Fair [5] Cloudy [6] Overcast [7] Rain [8] Storm    [9] Auto weather: {(autoWeather ? "on" : "off")}");
	}
}
