using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// Moves the sun and the moon through the day and sets the lights, the sky ambient and the cloud lighting to match.
// Unity's procedural skybox follows the sun by itself. NoiseController creates one of these when the scene has none.
public class DayNightCycle : MonoBehaviour {
	[Header("Time")]
	[SerializeField, Range(0, 24), Tooltip("Hour of the day, the sun rises at 6 and sets at 18.")]
	private float timeOfDay = 7f;
	[SerializeField, Min(0.1f), Tooltip("Real minutes for a whole day and night.")]
	private float dayLengthMinutes = 5f;
	[SerializeField] private bool paused;

	[Header("Sun")]
	[SerializeField, Tooltip("Uses the scene's sun (Lighting settings) when empty.")]
	private Light sun;
	[SerializeField, Range(1, 89), Tooltip("How high the sun gets at noon, in degrees.")]
	private float noonElevation = 60f;
	[SerializeField, Range(0, 360), Tooltip("Compass direction the sun rises in, 0 is +z and 90 is +x. 125 is roughly where the scene's sun already was.")]
	private float sunriseDirection = 125f;
	[SerializeField] private float sunIntensity = 2f;
	[SerializeField, Tooltip("Sun colour from the horizon (left) to straight up (right).")]
	private Gradient sunColor = MakeGradient(
		(0.00f, new Color(1.00f, 0.38f, 0.12f)),
		(0.05f, new Color(1.00f, 0.56f, 0.28f)),
		(0.15f, new Color(1.00f, 0.80f, 0.60f)),
		(0.35f, new Color(1.00f, 0.93f, 0.85f)),
		(1.00f, new Color(1.00f, 0.98f, 0.95f)));

	[Header("Moon")]
	[SerializeField] private bool  createMoon    = true;
	[SerializeField] private float moonIntensity = 0.15f;
	[SerializeField] private Color moonColor     = new Color(0.6f, 0.7f, 1.0f);

	[Header("Sky Light")]
	[SerializeField, Tooltip("Ambient light over the sun's elevation, from 20 degrees below the horizon (left) to straight up (right). Used as is, like the cloud Fog Color.")]
	private Gradient ambientColor = MakeGradient(
		(0.00f, new Color(0.015f, 0.020f, 0.040f)), // night
		(0.10f, new Color(0.040f, 0.045f, 0.090f)), // 9 degrees below, deep twilight
		(0.18f, new Color(0.220f, 0.180f, 0.260f)), // horizon, purple
		(0.23f, new Color(0.360f, 0.290f, 0.290f)), // sunrise, warm
		(0.36f, new Color(0.340f, 0.370f, 0.430f)), // day
		(1.00f, new Color(0.360f, 0.400f, 0.480f)));
	[SerializeField, Tooltip("Also light the scene with the sky colour. Replaces the skybox ambient, which doesn't update at runtime.")]
	private bool controlSceneAmbient = true;
	[SerializeField] private float sceneAmbientIntensity = 1f;

	private Light moon;
	private float messageUntil;

	// What NoiseController lights the clouds with: the sun during the day, the moon at night.
	public Transform CloudLight      { get; private set; }
	// Linear, relative to the noon sun.
	public Color     CloudLightColor { get; private set; }
	public Color     CloudAmbient    { get; private set; }
	public float     TimeOfDay       => timeOfDay;

	private void Awake() {
		if (!sun) sun = RenderSettings.sun;
		if (createMoon) {
			var moonObject = new GameObject("Moon");
			moonObject.transform.SetParent(transform, false);
			moon         = moonObject.AddComponent<Light>();
			moon.type    = LightType.Directional;
			moon.shadows = LightShadows.None;
		}
		Apply();
		// Show the controls for a bit at the start.
		messageUntil = Time.unscaledTime + 6;
	}

	private void Update() {
		float hoursPerSecond = paused ? 0 : 24f / (dayLengthMinutes * 60f);

		var keyboard = Keyboard.current;
		if (keyboard != null) {
			if (keyboard.pKey.wasPressedThisFrame) {
				paused       = !paused;
				messageUntil = Time.unscaledTime + 3;
			}
			// Holding [ or ] scrubs through the day at 2 hours per second.
			if (keyboard.rightBracketKey.isPressed) {
				hoursPerSecond += 2;
				messageUntil   =  Time.unscaledTime + 3;
			}
			if (keyboard.leftBracketKey.isPressed) {
				hoursPerSecond -= 2;
				messageUntil   =  Time.unscaledTime + 3;
			}
		}

		timeOfDay = Mathf.Repeat(timeOfDay + hoursPerSecond * Time.deltaTime, 24);
		Apply();
	}

	private void OnGUI() {
		if (Time.unscaledTime > messageUntil) return;
		int hours   = (int)timeOfDay;
		int minutes = (int)((timeOfDay - hours) * 60);
		GUI.Label(new Rect(10, Screen.height - 55, 800, 25),
		          $"Time {hours:00}:{minutes:00}{(paused ? " (paused)" : "")}    [P] pause    hold [ or ] to scrub through the day");
	}

	private void Apply() {
		// The sun goes round a circle: rises at 6, highest at 12, sets at 18. The circle is tilted so noon
		// reaches noonElevation, then turned so the sun rises in sunriseDirection.
		float dayAngle = (timeOfDay / 12f - 0.5f) * Mathf.PI;
		float tilt     = noonElevation * Mathf.Deg2Rad;
		Vector3 toSun = Quaternion.Euler(0, sunriseDirection - 90f, 0) *
		                new Vector3(Mathf.Cos(dayAngle), Mathf.Sin(dayAngle) * Mathf.Sin(tilt), Mathf.Sin(dayAngle) * Mathf.Cos(tilt));
		float sunHeight = toSun.y; // sine of the elevation
		float elevation = Mathf.Asin(Mathf.Clamp(sunHeight, -1, 1)) * Mathf.Rad2Deg;

		// Both lights are off around the horizon, so switching the clouds from one to the other doesn't pop.
		float sunUp   = Smooth(-0.05f, 0.10f, sunHeight);
		float moonUp  = Smooth(0.05f, 0.20f, -sunHeight);
		Color sunTint = sunColor.Evaluate(Mathf.Clamp01(elevation / 90f));

		if (sun) {
			sun.transform.rotation = Quaternion.LookRotation(-toSun);
			sun.color              = sunTint;
			sun.intensity          = sunIntensity * sunUp;
		}
		if (moon) {
			moon.transform.rotation = Quaternion.LookRotation(toSun);
			moon.color              = moonColor;
			moon.intensity          = moonIntensity * moonUp;
			moon.enabled            = moonUp > 0;
		}

		bool sunLightsClouds = sunHeight > -0.05f || !moon;
		CloudLight      = sunLightsClouds ? (sun ? sun.transform : null) : moon.transform;
		CloudLightColor = sunLightsClouds ? sunTint.linear * sunUp
		                                  : moonColor.linear * (moonUp * moonIntensity / Mathf.Max(sunIntensity, 1e-4f));
		CloudAmbient    = ambientColor.Evaluate(Mathf.Clamp01((elevation + 20f) / 110f));

		if (controlSceneAmbient) {
			Color ambient = CloudAmbient * sceneAmbientIntensity;
			RenderSettings.ambientMode  = AmbientMode.Flat;
			RenderSettings.ambientLight = ambient;
			// URP reads the ambient from the probe, so set that too.
			var probe = new SphericalHarmonicsL2();
			probe.AddAmbientLight(ambient);
			RenderSettings.ambientProbe = probe;
		}
	}

	private static float Smooth(float edge0, float edge1, float x) {
		float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
		return t * t * (3 - 2 * t);
	}

	private static Gradient MakeGradient(params (float time, Color color)[] keys) {
		var colorKeys = new GradientColorKey[keys.Length];
		for (int i = 0; i < keys.Length; i++) colorKeys[i] = new GradientColorKey(keys[i].color, keys[i].time);
		var gradient = new Gradient();
		gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
		return gradient;
	}
}
