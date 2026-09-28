using System;
using System.Collections.Generic;
using GrayWolf.GPUInstancing.Domain;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Random = UnityEngine.Random;

[Serializable]
class CloudType {
	[SerializeField] private string prefix;


	public float          startAltitude;
	public float          height;
	public float          density;
	public float          coverage;
	public AnimationCurve shapeCurve;
	public AnimationCurve densityCurve;

	private int       floatsID, shapeCurveID, densityCurveID;
	private Texture2D shapeLut, densityLut;
	private bool      curvesDirty;

	public void Init() {
		floatsID       = Shader.PropertyToID(prefix + "_floats");
		shapeCurveID   = Shader.PropertyToID(prefix + "_shape_lut");
		densityCurveID = Shader.PropertyToID(prefix + "_density_lut");

		shapeLut    = CreateLut();
		densityLut  = CreateLut();
		curvesDirty = true;
	}

	public void MakeCurvesDirty() => curvesDirty = true;

	public void Release() {
		if (shapeLut) UnityEngine.Object.Destroy(shapeLut);
		if (densityLut) UnityEngine.Object.Destroy(densityLut);
	}

	public void SetValues(ComputeShader cs) {
		if (curvesDirty) {
			GenCurveLut(shapeLut,   shapeCurve);
			GenCurveLut(densityLut, densityCurve);
			curvesDirty = false;
		}

		cs.SetVector(floatsID, new Vector4(startAltitude, height, density, coverage));
		cs.SetTexture(0, shapeCurveID,   shapeLut);
		cs.SetTexture(0, densityCurveID, densityLut);
		cs.SetTexture(2, shapeCurveID,   shapeLut);
		cs.SetTexture(2, densityCurveID, densityLut);
	}

	private void GenCurveLut(Texture2D tex, AnimationCurve curve) {
		var data = new float[128];
		for (int i = 0; i < 128; i++) {
			data[i] = curve.Evaluate((i + 0.5f) / 128);
		}

		tex.SetPixelData(data, 0);
		tex.Apply(false);
	}

	private Texture2D CreateLut() {
		return new Texture2D(128, 1, TextureFormat.RFloat, false, true) {
			wrapMode   = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear,
		};
	}
}

[Serializable]
class NoiseParams {
	[SerializeField] private string prefix;

	[SerializeField, Range(2, 128)] public float frequency;
	[SerializeField, Range(0, 1)]   public float gain;
	[SerializeField, Range(1, 3)]   public float lacunarity;
	[SerializeField, Range(1, 8)]   public int   octaves;

	private int floatsID;

	public void Init() {
		floatsID = Shader.PropertyToID(prefix + "_floats");
	}

	public void SetValues(ComputeShader cs) {
		cs.SetVector(floatsID, new Vector4(frequency, gain, lacunarity, octaves));
	}
}

public class NoiseController : MonoBehaviour {
	[Header("Volumetrics")]
	[Header("-Quality")]
	[SerializeField]
	private int textureDivide = 2;
	[SerializeField] private float stepSize            = 1.0f;
	[SerializeField] private int   firstPassStepAmount = 1;
	[SerializeField] private int   stepAmount          = 1;
	[SerializeField] private float maxDist             = 100;
	[SerializeField] private bool  firstPass;
	[SerializeField] private bool  useStepSize;
	[SerializeField] private bool  temporalUpscaling;
	[SerializeField, Range(0.02f, 1), Tooltip("How much of the new frame goes into the result. Lower is smoother but reacts slower, 1 turns temporal accumulation off.")]
	private float temporalBlend = 0.1f;

	[Header("-Settings")]
	[SerializeField] private float coverage;
	[SerializeField] private int   currentType = 3;
	[SerializeField] private float fullDensityMult;
	[SerializeField] private float densityMultiplier = 1.0f;
	[SerializeField] private float fbmMult           = 1.0f;
	[SerializeField] private float densityThreshold  = 1.0f;
	[SerializeField] private float lightScattering   = 1.0f;
	[SerializeField] private float noiseSize         = 1.0f;
	[SerializeField] private float detailSize        = 1.0f;
	[SerializeField] private float detailStrength    = 1.0f;
	[SerializeField] private float detail1Weight;
	[SerializeField] private float detail2Weight;
	[SerializeField] private float detail3Weight;
	[SerializeField] private float smallDetailStrength;
	[SerializeField] private float smallDetail1Weight;
	[SerializeField] private float smallDetail2Weight;
	[SerializeField] private float smallDetail3Weight;
	[SerializeField] private float yMin          = 60;
	[SerializeField] private float yMax          = 70;
	[SerializeField] private float shadowDensity = 1.0f;
	[SerializeField] private float shadowStepSize;
	[SerializeField] private float shadowConeSpread;
	[SerializeField] private float gradient     = 0.2f;
	[SerializeField] private float squishFactor = 2f;
	[SerializeField] private bool  useBoundingSphere;
	[SerializeField] private float sphereMinRadius = 1.0f;
	[SerializeField] private float sphereMaxRadius = 1.0f;
	[SerializeField] private float cumulusYMin     = 1500f;
	[SerializeField] private float cumulusYMax     = 2500f;
	[SerializeField] private float baseSpeed;
	[SerializeField] private float detailSpeed;
	[SerializeField] private float weatherSpeed;
	[SerializeField] private bool  combineBounds = true;
	[SerializeField] private Color fogColor;
	[SerializeField] private Color fogColorNight;
	[ColorUsage(true, true)] [SerializeField]
	private Color lightContribution;
	[ColorUsage(true, true)] [SerializeField]
	private Color lightContributionSunset;
	[SerializeField] private Texture2D coverageTexture, CumulusLut;
	[SerializeField] private Collider  bounds;
	[SerializeField] private Transform sphereCenter;

	[Header("-Lighting")]
	[SerializeField, Range(-0.9f, 0), Tooltip("How strongly the second phase lobe scatters light backwards.")]
	private float backScattering = -0.3f;
	[SerializeField, Range(0, 1), Tooltip("Blend between the forward lobe (Light Scattering) and the back lobe.")]
	private float backScatteringWeight = 0.25f;
	[SerializeField, Range(0, 1), Tooltip("Ambient light at the bottom of the clouds compared to the top.")]
	private float ambientBottom = 0.4f;
	[SerializeField, Range(0, 1), Tooltip("Darkens cloud edges when the sun is behind the camera.")]
	private float powderStrength = 0.5f;
	[SerializeField, Tooltip("Distance in meters where the clouds have faded to ~37%.")]
	private float horizonFade = 60000f;
	[SerializeField, Tooltip("How far in meters the cloud tops lean with the wind. Flip the sign if Base Speed is negative.")]
	private float windShear = 1000f;

	[Header("Cloud Types")]
	[SerializeField] private CloudType stratus;
	[SerializeField] private CloudType   stratocumulus;
	[SerializeField] private CloudType   cumulus;
	private                  CloudType[] cloudTypes;

	[Header("Noise")]
	[SerializeField] private bool regenerateNoise = false;
	[SerializeField] private bool constantlyGenerateNoise = false;
	[Header("-Settings")]
	[SerializeField] private NoiseParams perlinWorleyPerlin;
	[SerializeField] private NoiseParams   perlinWorleyWorley;
	[SerializeField] private NoiseParams   perlinWorley1;
	[SerializeField] private NoiseParams   perlinWorley2;
	[SerializeField] private NoiseParams   perlinWorley3;
	[SerializeField] private NoiseParams   weatherNoise;
	private                  NoiseParams[] noiseParams;

	[Header("--Texture 2 Worley")]
	[SerializeField, Range(0, 128)] private float t2W1Size = 1.0f;
	[SerializeField, Range(0, 128)] private float t2W2Size = 1.0f;
	[SerializeField, Range(0, 128)] private float t2W3Size = 1.0f;

	[Header("Shadows")]
	[SerializeField] private float shadowWorldSize = 1000;
	[SerializeField] private int shadowSteps = 16;


	[Header("Other")]
	[SerializeField] private ComputeShader noiseShader, volumetricsShader;
	[SerializeField] private RawImage noiseDisplay;

	[SerializeField] private RenderTexture perlinRenderTexture,
	                                       worleyRenderTexture,
	                                       weatherRenderTexture,
	                                       shadowRT;
	private Light sun;

	[SerializeField] private uint currentPixel = 0;

	private Matrix4x4 oldProjectionMatrix;

	private bool          firstFrame = true, curvesDirty = true;
	private ComputeBuffer minMaxValues;

	// This frame's raymarch (a quarter of the size with temporal upscaling) and two resolved textures that swap every frame.
	private readonly RenderTexture[] historyRTs = new RenderTexture[2];
	private          RenderTexture   currentRT;
	private          int             historyIndex, frameIndex;
	private          bool            historyValid, temporalEnabled = true;
	private          float           toggleMessageUntil;

	// Kernel order in VolumetricCompute.compute.
	private const int KernelMain = 0, KernelResolve = 1, KernelShadows = 2;


	#region Caches

	private static readonly int PerlinTex                   = Shader.PropertyToID("PerlinTex");
	private static readonly int WorleyTex                   = Shader.PropertyToID("WorleyTex");
	private static readonly int WorleyTex1                  = Shader.PropertyToID("_WorleyTex");
	private static readonly int PerlinTex1                  = Shader.PropertyToID("_PerlinTex");
	private static readonly int Result                      = Shader.PropertyToID("Result");
	private static readonly int DepthTex                    = Shader.PropertyToID("DepthTex");
	private static readonly int VolumetricsTex              = Shader.PropertyToID("_VolumetricsTex");
	private static readonly int CamPos                      = Shader.PropertyToID("_CamPos");
	private static readonly int FogColor                    = Shader.PropertyToID("fog_base_color_day");
	private static readonly int MainLightColor              = Shader.PropertyToID("_MainLightColor");
	private static readonly int LightDirection              = Shader.PropertyToID("_LightDirection");
	private static readonly int LightContribution           = Shader.PropertyToID("light_contribution_day");
	private static readonly int MinBounds                   = Shader.PropertyToID("_MinBounds");
	private static readonly int MaxBounds                   = Shader.PropertyToID("_MaxBounds");
	private static readonly int Time1                       = Shader.PropertyToID("_Time");
	private static readonly int DensityMultiplier           = Shader.PropertyToID("_DensityMultiplier");
	private static readonly int DensityThreshold            = Shader.PropertyToID("_DensityThreshold");
	private static readonly int LightScattering             = Shader.PropertyToID("_LightScattering");
	private static readonly int StepSize                    = Shader.PropertyToID("_StepSize");
	private static readonly int NoiseSize                   = Shader.PropertyToID("_NoiseSize");
	private static readonly int DetailSize                  = Shader.PropertyToID("_DetailSize");
	private static readonly int DetailStrength              = Shader.PropertyToID("_DetailStrength");
	private static readonly int MaxDistance                 = Shader.PropertyToID("_MaxDistance");
	private static readonly int ShadowDensity               = Shader.PropertyToID("_ShadowDensity");
	private static readonly int Gradient1                   = Shader.PropertyToID("_Gradient");
	private static readonly int FbmMult                     = Shader.PropertyToID("_FBMMult");
	private static readonly int FirstPassStepAmount         = Shader.PropertyToID("_FirstPassStepAmount");
	private static readonly int StepAmount                  = Shader.PropertyToID("_StepAmount");
	private static readonly int FirstPass                   = Shader.PropertyToID("_FirstPass");
	private static readonly int UseStepSize                 = Shader.PropertyToID("_UseStepSize");
	private static readonly int InvVp                       = Shader.PropertyToID("_InvVP");
	private static readonly int SphereMinRadius             = Shader.PropertyToID("_SphereMinRadius");
	private static readonly int SphereMaxRadius             = Shader.PropertyToID("_SphereMaxRadius");
	private static readonly int SphereCenter                = Shader.PropertyToID("_SphereCenter");
	private static readonly int UseBoundingSphere           = Shader.PropertyToID("_UseBoundingSphere");
	private static readonly int SquishFactor                = Shader.PropertyToID("_SquishFactor");
	private static readonly int T2W1Size                    = Shader.PropertyToID("w_worley_1_base_frequency_global");
	private static readonly int T2W2Size                    = Shader.PropertyToID("w_worley_2_base_frequency_global");
	private static readonly int T2W3Size                    = Shader.PropertyToID("w_worley_3_base_frequency_global");
	private static readonly int CumulusYMin                 = Shader.PropertyToID("cumulus_y_min");
	private static readonly int CumulusYMax                 = Shader.PropertyToID("cumulus_y_max");
	private static readonly int CombineBounds               = Shader.PropertyToID("combine_bounds");
	private static readonly int DetailFbmMult               = Shader.PropertyToID("detail_fbm_mult");
	private static readonly int DetailFbmWeight1            = Shader.PropertyToID("detail_fbm_weight1");
	private static readonly int DetailFbmWeight2            = Shader.PropertyToID("detail_fbm_weight2");
	private static readonly int DetailFbmWeight3            = Shader.PropertyToID("detail_fbm_weight3");
	private static readonly int DetailMult                  = Shader.PropertyToID("detail_mult");
	private static readonly int DetailWeight1               = Shader.PropertyToID("detail_weight1");
	private static readonly int DetailWeight2               = Shader.PropertyToID("detail_weight2");
	private static readonly int DetailWeight3               = Shader.PropertyToID("detail_weight3");
	private static readonly int FullDensityMult             = Shader.PropertyToID("full_density_mult");
	private static readonly int Time                        = Shader.PropertyToID("time");
	private static readonly int BaseSpeed                   = Shader.PropertyToID("base_speed");
	private static readonly int DetailSpeed                 = Shader.PropertyToID("detail_speed");
	private static readonly int ShadowStepSize              = Shader.PropertyToID("shadow_step_size");
	private static readonly int ShadowConeSpread            = Shader.PropertyToID("shadow_cone_spread");
	private static readonly int LightContributionSunset     = Shader.PropertyToID("light_contribution_sunset");
	private static readonly int FogBaseColorNight           = Shader.PropertyToID("fog_base_color_night");
	private static readonly int PixelOffset                 = Shader.PropertyToID("pixel_offset");
	private static readonly int UseTemporalUpscaling        = Shader.PropertyToID("use_temporal_upscaling");
	private static readonly int Coverage                    = Shader.PropertyToID("coverage");
	private static readonly int CloudTypeStratus            = Shader.PropertyToID("cloud_type_stratus");
	private static readonly int CloudTypeCumulus            = Shader.PropertyToID("cloud_type_cumulus");
	private static readonly int CloudTypeCumulonimbus       = Shader.PropertyToID("cloud_type_cumulonimbus");
	private static readonly int CurrentCloudType            = Shader.PropertyToID("current_cloud_type");
	private static readonly int WeatherTexture              = Shader.PropertyToID("weatherTexture");
	private static readonly int WeatherMap                  = Shader.PropertyToID("weather_map");
	private static readonly int PrevVp                      = Shader.PropertyToID("prev_vp");
	private static readonly int RendertextureOld            = Shader.PropertyToID("rendertexture_old");
	private static readonly int ShadowRT                    = Shader.PropertyToID("shadowRT");
	private static readonly int ShadowResolution1           = Shader.PropertyToID("shadow_resolution");
	private static readonly int ShadowSteps                 = Shader.PropertyToID("shadow_steps");
	private static readonly int ShadowWorldSize             = Shader.PropertyToID("shadow_world_size");
	private static readonly int WorldSize                   = Shader.PropertyToID("_ShadowWorldSize");
	private static readonly int PwPerlinBaseFrequencyGlobal = Shader.PropertyToID("pw_perlin_base_frequency_global");
	private static readonly int PwPerlinLacunarityGlobal    = Shader.PropertyToID("pw_perlin_lacunarity_global");
	private static readonly int PwPerlinGainGlobal          = Shader.PropertyToID("pw_perlin_gain_global");
	private static readonly int PwPerlinOctavesGlobal       = Shader.PropertyToID("pw_perlin_octaves_global");
	private static readonly int PwWorleyBaseFrequencyGlobal = Shader.PropertyToID("pw_worley_base_frequency_global");
	private static readonly int PwWorleyLacunarityGlobal    = Shader.PropertyToID("pw_worley_lacunarity_global");
	private static readonly int PwWorleyGainGlobal          = Shader.PropertyToID("pw_worley_gain_global");
	private static readonly int PwWorleyOctavesGlobal       = Shader.PropertyToID("pw_worley_octaves_global");
	private static readonly int Lut                         = Shader.PropertyToID("cumulus_lut");
	private static readonly int MinMaxBuffer                = Shader.PropertyToID("min_max_buffer");
	private static readonly int PWorley1BaseFrequencyGlobal = Shader.PropertyToID("p_worley_1_base_frequency_global");
	private static readonly int PWorley1LacunarityGlobal    = Shader.PropertyToID("p_worley_1_lacunarity_global");
	private static readonly int PWorley1GainGlobal          = Shader.PropertyToID("p_worley_1_gain_global");
	private static readonly int PWorley1OctavesGlobal       = Shader.PropertyToID("p_worley_1_octaves_global");
	private static readonly int PWorley2BaseFrequencyGlobal = Shader.PropertyToID("p_worley_2_base_frequency_global");
	private static readonly int PWorley2LacunarityGlobal    = Shader.PropertyToID("p_worley_2_lacunarity_global");
	private static readonly int PWorley2GainGlobal          = Shader.PropertyToID("p_worley_2_gain_global");
	private static readonly int PWorley2OctavesGlobal       = Shader.PropertyToID("p_worley_2_octaves_global");
	private static readonly int PWorley3BaseFrequencyGlobal = Shader.PropertyToID("p_worley_3_base_frequency_global");
	private static readonly int PWorley3LacunarityGlobal    = Shader.PropertyToID("p_worley_3_lacunarity_global");
	private static readonly int PWorley3GainGlobal          = Shader.PropertyToID("p_worley_3_gain_global");
	private static readonly int PWorley3OctavesGlobal       = Shader.PropertyToID("p_worley_3_octaves_global");
	private static readonly int WeatherSpeed                = Shader.PropertyToID("weather_speed");
	private static readonly int BackScattering              = Shader.PropertyToID("back_scattering");
	private static readonly int BackScatteringWeight        = Shader.PropertyToID("back_scattering_weight");
	private static readonly int AmbientBottom               = Shader.PropertyToID("ambient_bottom");
	private static readonly int PowderStrength              = Shader.PropertyToID("powder_strength");
	private static readonly int HorizonFade                 = Shader.PropertyToID("horizon_fade");
	private static readonly int WindShear                   = Shader.PropertyToID("wind_shear");
	private static readonly int CurrentFrame                = Shader.PropertyToID("current_frame");
	private static readonly int CloudSize                   = Shader.PropertyToID("cloud_size");
	private static readonly int FrameIndex                  = Shader.PropertyToID("frame_index");
	private static readonly int HistoryValid                = Shader.PropertyToID("history_valid");
	private static readonly int TemporalBlend               = Shader.PropertyToID("temporal_blend");
	private static readonly int WindOffset                  = Shader.PropertyToID("wind_offset");
	private static readonly int ShadowCenter                = Shader.PropertyToID("shadow_center");
	private static readonly int CloudShadowCenter           = Shader.PropertyToID("_CloudShadowCenter");
	private static readonly int SkyDepth                    = Shader.PropertyToID("sky_depth");

	#endregion

	#region Unity Functions

	private void OnEnable()  => RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
	private void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

	private void Start() {
		cloudTypes = new CloudType[] { stratus, stratocumulus, cumulus };
		noiseParams = new NoiseParams[] {perlinWorleyPerlin, perlinWorleyWorley, perlinWorley1, perlinWorley2, perlinWorley3 };

		foreach (var v in cloudTypes) v.Init();
		foreach (var v in noiseParams) v.Init();
		

		minMaxValues = new ComputeBuffer(2, sizeof(int));

		InitializeWeather();

		InitializeNoise();

		InitializeVolumetrics();
	}

	private void OnValidate() {
		if (cloudTypes == null) return;
		foreach (var v in cloudTypes) v?.MakeCurvesDirty();
	}

	private void Update() {
		CheckToggleKeys();

		if (!regenerateNoise && !constantlyGenerateNoise) return;
		DispatchWeather();
		DispatchNoise();
		regenerateNoise = false;
	}

	private void OnDestroy() {
		ReleaseCloudTextures();
		foreach (var rt in new[] { perlinRenderTexture, worleyRenderTexture, weatherRenderTexture, shadowRT }) DestroyTexture(rt);
		if (cloudTypes != null) foreach (var v in cloudTypes) v?.Release();
		if (CumulusLut) Destroy(CumulusLut);
		minMaxValues?.Release();
	}

	// T and U toggle the temporal features in a build, so they can be compared without the editor.
	private void CheckToggleKeys() {
		var keyboard = Keyboard.current;
		if (keyboard == null) return;

		if (keyboard.tKey.wasPressedThisFrame) {
			temporalEnabled = !temporalEnabled;
			toggleMessageUntil = UnityEngine.Time.unscaledTime + 3;
		}
		if (keyboard.uKey.wasPressedThisFrame) {
			temporalUpscaling = !temporalUpscaling;
			toggleMessageUntil = UnityEngine.Time.unscaledTime + 3;
		}
	}

	private void OnGUI() {
		if (UnityEngine.Time.unscaledTime > toggleMessageUntil) return;
		GUI.Label(new Rect(10, Screen.height - 30, 800, 25),
		          $"[T] Temporal accumulation: {(temporalEnabled ? "on" : "off")}    [U] Temporal upscaling: {(temporalUpscaling ? "on" : "off")}");
	}


	private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam) {
		if (cam != Camera.main) return;
		DispatchVolumetrics();
	}

	#endregion

	#region Custom Functions

	#region Noise Functions

	private void InitializeNoise() {
		InitializeWorley();
		InitializePerlin();

		noiseShader.SetTexture(0, PerlinTex, perlinRenderTexture);
		noiseShader.SetTexture(1, WorleyTex, worleyRenderTexture);

		DispatchNoise();
	}


	private void DispatchNoise() {
		int[] minMax = { int.MaxValue, int.MinValue };
		minMaxValues.SetData(minMax);

		noiseShader.SetBuffer(0, MinMaxBuffer, minMaxValues);
		noiseShader.SetBuffer(1, MinMaxBuffer, minMaxValues);

		foreach(var v in noiseParams) v.SetValues(noiseShader);

		noiseShader.SetFloat(T2W1Size, t2W1Size);
		noiseShader.SetFloat(T2W2Size, t2W2Size);
		noiseShader.SetFloat(T2W3Size, t2W3Size);


		noiseShader.Dispatch(0, perlinRenderTexture.width    / 8, perlinRenderTexture.height / 8,
		                     perlinRenderTexture.volumeDepth / 8);
		noiseShader.Dispatch(1, worleyRenderTexture.width    / 8, worleyRenderTexture.height / 8,
		                     worleyRenderTexture.volumeDepth / 8);

		int[] readBuffer = new int[2];
		minMaxValues.GetData(readBuffer);
		Debug.Log(readBuffer[0] / 10000.0f + " " + readBuffer[1] / 10000.0f);
	}

	private void InitializeWorley() {
		worleyRenderTexture = new RenderTexture(128, 128, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		worleyRenderTexture.dimension = TextureDimension.Tex3D;
		worleyRenderTexture.volumeDepth = 128;
		worleyRenderTexture.enableRandomWrite = true;
		worleyRenderTexture.wrapMode = TextureWrapMode.Repeat;
		worleyRenderTexture.Create();
		Shader.SetGlobalTexture(WorleyTex1, worleyRenderTexture);
	}

	private void InitializePerlin() {
		perlinRenderTexture = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
		perlinRenderTexture.dimension = TextureDimension.Tex3D;
		perlinRenderTexture.volumeDepth = 256;
		perlinRenderTexture.enableRandomWrite = true;
		perlinRenderTexture.wrapMode = TextureWrapMode.Repeat;
		perlinRenderTexture.Create();
		Shader.SetGlobalTexture(PerlinTex1, perlinRenderTexture);
	}

	#endregion

	#region Volumetrics Functions

	private void InitializeVolumetrics() {
		sun = RenderSettings.sun;

		CreateShadowTexture();
		CreateCurveLuts();

		volumetricsShader.SetTexture(KernelMain,    WeatherMap, weatherRenderTexture);
		volumetricsShader.SetTexture(KernelMain,    PerlinTex1, perlinRenderTexture);
		volumetricsShader.SetTexture(KernelMain,    WorleyTex1, worleyRenderTexture);
		volumetricsShader.SetTexture(KernelMain,    Lut,        CumulusLut);
		volumetricsShader.SetTexture(KernelShadows, WeatherMap, weatherRenderTexture);
		volumetricsShader.SetTexture(KernelShadows, PerlinTex1, perlinRenderTexture);
		volumetricsShader.SetTexture(KernelShadows, WorleyTex1, worleyRenderTexture);
		volumetricsShader.SetTexture(KernelShadows, Lut,        CumulusLut);
	}

	// (Re)creates the cloud textures whenever the resolution or the temporal upscaling setting changes.
	private void EnsureCloudTextures(int screenWidth, int screenHeight) {
		int divide        = Mathf.Max(1, textureDivide);
		int width         = Mathf.Max(1, screenWidth  / divide);
		int height        = Mathf.Max(1, screenHeight / divide);
		int currentWidth  = temporalUpscaling ? (width  + 3) / 4 : width;
		int currentHeight = temporalUpscaling ? (height + 3) / 4 : height;

		if (currentRT && currentRT.width == currentWidth && currentRT.height == currentHeight &&
		    historyRTs[0] && historyRTs[0].width == width && historyRTs[0].height == height) return;

		ReleaseCloudTextures();
		currentRT     = CreateCloudTexture(currentWidth, currentHeight);
		historyRTs[0] = CreateCloudTexture(width, height);
		historyRTs[1] = CreateCloudTexture(width, height);
		historyValid  = false;
	}

	private static RenderTexture CreateCloudTexture(int width, int height) {
		// Half precision is plenty for cloud colours and halves the memory traffic of ARGBFloat.
		var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf) {
			enableRandomWrite = true,
			// Clamp, with Repeat the bilinear upsampling pulled in clouds from the opposite screen edge.
			wrapMode   = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear,
		};
		rt.Create();
		return rt;
	}

	private void ReleaseCloudTextures() {
		DestroyTexture(currentRT);
		DestroyTexture(historyRTs[0]);
		DestroyTexture(historyRTs[1]);
		currentRT     = null;
		historyRTs[0] = historyRTs[1] = null;
	}

	private static void DestroyTexture(RenderTexture rt) {
		if (!rt) return;
		rt.Release();
		Destroy(rt);
	}

	private static int ThreadGroups(int size) => (size + 7) / 8;

	private void CreateShadowTexture() {
		shadowRT = new RenderTexture(128, 128, 0, RenderTextureFormat.RHalf);

		shadowRT.enableRandomWrite = true;
		shadowRT.wrapMode          = TextureWrapMode.Clamp;
		shadowRT.filterMode        = FilterMode.Bilinear;
		shadowRT.Create();

		Shader.SetGlobalTexture(ShadowRT, shadowRT);
	}

	private void CreateCurveLuts() {
		CumulusLut = new Texture2D(128, 1, TextureFormat.RFloat, false, true) {
			wrapMode   = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear,
		};
	}

	private void DispatchVolumetrics() {
		currentPixel = (currentPixel + 1) % 16;

		var cam      = Camera.main;
		var depthTex = PersistentDepthFeature.PersistentDepthTexture;

		if (depthTex != null && depthTex.rt && cam) {
			// The depth texture has the real render resolution, so resizing the window just works.
			EnsureCloudTextures(depthTex.rt.width, depthTex.rt.height);
			var historyRead  = historyRTs[historyIndex];
			var historyWrite = historyRTs[1 - historyIndex];

			volumetricsShader.SetTexture(KernelMain,    DepthTex,         depthTex.rt);
			volumetricsShader.SetTexture(KernelMain,    Result,           currentRT);
			volumetricsShader.SetTexture(KernelResolve, CurrentFrame,     currentRT);
			volumetricsShader.SetTexture(KernelResolve, RendertextureOld, historyRead);
			volumetricsShader.SetTexture(KernelResolve, Result,           historyWrite);
			volumetricsShader.SetTexture(KernelShadows, ShadowRT,         shadowRT);
			volumetricsShader.SetVector(CloudSize, new Vector4(historyWrite.width, historyWrite.height, currentRT.width, currentRT.height));

			// The jitter only moves when something averages it, otherwise it would crawl.
			float blend = temporalEnabled ? temporalBlend : 1.0f;
			frameIndex++;
			volumetricsShader.SetInt(FrameIndex, blend < 1.0f || temporalUpscaling ? frameIndex : 0);
			volumetricsShader.SetFloat(TemporalBlend, blend);
			volumetricsShader.SetBool(HistoryValid, historyValid);
			// The base noise scrolls by baseSpeed noise tiles per second towards -x, so last frame the clouds were further along +x.
			volumetricsShader.SetVector(WindOffset, new Vector4(baseSpeed * noiseSize * UnityEngine.Time.deltaTime, 0, 0, 0));
			volumetricsShader.SetFloat(SkyDepth, SystemInfo.usesReversedZBuffer ? 0.0f : 1.0f);

			// Snapped to whole texels so the ground shadows don't shimmer while the camera moves.
			float shadowTexel  = shadowWorldSize / shadowRT.width;
			var   camPosition  = cam.transform.position;
			var   shadowCenter = new Vector4(Mathf.Floor(camPosition.x / shadowTexel) * shadowTexel,
			                                 Mathf.Floor(camPosition.z / shadowTexel) * shadowTexel, 0, 0);
			volumetricsShader.SetVector(ShadowCenter, shadowCenter);
			Shader.SetGlobalVector(CloudShadowCenter, shadowCenter);

			volumetricsShader.SetVector(CamPos,
			                            new Vector4(cam.transform.position.x, cam.transform.position.y,
			                                        cam.transform.position.z, 0.0f));

			volumetricsShader.SetVector(FogColor,          fogColor);
			volumetricsShader.SetVector(FogBaseColorNight, fogColorNight);

			volumetricsShader.SetVector(LightContribution,       lightContribution);
			volumetricsShader.SetVector(LightContributionSunset, lightContributionSunset);

			volumetricsShader.SetVector(MainLightColor, sun.color.linear);
			volumetricsShader.SetVector(LightDirection, sun.transform.forward);

			volumetricsShader.SetVector(MinBounds, bounds.bounds.min);
			volumetricsShader.SetVector(MaxBounds, bounds.bounds.max);
			volumetricsShader.SetVector(SphereCenter,
			                            new Vector4(sphereCenter.position.x, sphereCenter.position.y,
			                                        sphereCenter.position.z, 0.0f));

			volumetricsShader.SetFloat(DensityMultiplier, densityMultiplier);
			volumetricsShader.SetFloat(DensityThreshold,  densityThreshold);
			volumetricsShader.SetFloat(LightScattering,   lightScattering);
			volumetricsShader.SetFloat(NoiseSize,         noiseSize);
			volumetricsShader.SetFloat(DetailSize,        detailSize);
			volumetricsShader.SetFloat(MaxDistance,       maxDist);
			volumetricsShader.SetFloat(ShadowDensity,     shadowDensity);
			volumetricsShader.SetFloat(Gradient1,         gradient);
			volumetricsShader.SetFloat(SphereMinRadius,   sphereMinRadius);
			volumetricsShader.SetFloat(SphereMaxRadius,   sphereMaxRadius);
			volumetricsShader.SetFloat(SquishFactor,      squishFactor);
			volumetricsShader.SetFloat(CumulusYMin,       cumulusYMin);
			volumetricsShader.SetFloat(CumulusYMax,       cumulusYMax);
			volumetricsShader.SetFloat(DetailFbmMult,     detailStrength);
			volumetricsShader.SetFloat(DetailFbmWeight1,  detail1Weight);
			volumetricsShader.SetFloat(DetailFbmWeight2,  detail2Weight);
			volumetricsShader.SetFloat(DetailFbmWeight3,  detail3Weight);
			volumetricsShader.SetFloat(DetailMult,        smallDetailStrength);
			volumetricsShader.SetFloat(DetailWeight1,     smallDetail1Weight);
			volumetricsShader.SetFloat(DetailWeight2,     smallDetail2Weight);
			volumetricsShader.SetFloat(DetailWeight3,     smallDetail3Weight);
			volumetricsShader.SetFloat(FullDensityMult,   fullDensityMult);
			volumetricsShader.SetFloat(Time,              UnityEngine.Time.time);
			volumetricsShader.SetFloat(BaseSpeed,         baseSpeed);
			volumetricsShader.SetFloat(DetailSpeed,       detailSpeed);
			volumetricsShader.SetFloat(WeatherSpeed, weatherSpeed);
			volumetricsShader.SetFloat(ShadowStepSize,    shadowStepSize);
			volumetricsShader.SetFloat(ShadowConeSpread,  shadowConeSpread);
			volumetricsShader.SetFloat(BackScattering,    backScattering);
			volumetricsShader.SetFloat(BackScatteringWeight, backScatteringWeight);
			volumetricsShader.SetFloat(AmbientBottom,     ambientBottom);
			volumetricsShader.SetFloat(PowderStrength,    powderStrength);
			volumetricsShader.SetFloat(HorizonFade,       horizonFade);
			volumetricsShader.SetFloat(WindShear,         windShear);
			volumetricsShader.SetFloat(Coverage,          coverage);
			volumetricsShader.SetInt(CurrentCloudType, currentType);
			volumetricsShader.SetFloat(ShadowResolution1, shadowRT.width);
			volumetricsShader.SetFloat(ShadowWorldSize,   shadowWorldSize);
			Shader.SetGlobalFloat(WorldSize, shadowWorldSize);

			volumetricsShader.SetInt(StepAmount,  math.max(stepAmount, 1));
			volumetricsShader.SetInt(PixelOffset, (int)currentPixel);
			volumetricsShader.SetInt(ShadowSteps, shadowSteps);

			volumetricsShader.SetBool(UseStepSize,          useStepSize);
			volumetricsShader.SetBool(UseBoundingSphere,    useBoundingSphere);
			volumetricsShader.SetBool(CombineBounds,        combineBounds);
			volumetricsShader.SetBool(UseTemporalUpscaling, temporalUpscaling);

			var proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
			var view = cam.worldToCameraMatrix;
			proj[1, 1] = -proj[1, 1];
			var vp = proj * view;
			if (firstFrame) {
				oldProjectionMatrix = vp;
				firstFrame          = false;
			}

			volumetricsShader.SetMatrix(InvVp,  vp.inverse);
			volumetricsShader.SetMatrix(PrevVp, oldProjectionMatrix);

			foreach (var v in cloudTypes) v.SetValues(volumetricsShader);

			// Rounded up, the kernels skip the threads that fall outside the textures.
			volumetricsShader.Dispatch(KernelMain,    ThreadGroups(currentRT.width),    ThreadGroups(currentRT.height),    1);
			volumetricsShader.Dispatch(KernelResolve, ThreadGroups(historyWrite.width), ThreadGroups(historyWrite.height), 1);
			volumetricsShader.Dispatch(KernelShadows, ThreadGroups(shadowRT.width),     ThreadGroups(shadowRT.height),     1);

			oldProjectionMatrix = vp;

			Shader.SetGlobalTexture(VolumetricsTex, historyWrite);

			historyIndex = 1 - historyIndex;
			historyValid = true;
		}
	}

	#endregion

	#region Weather Functions

	private void InitializeWeather() {
		weatherNoise.Init();
		InitializeWeatherTexture();
		DispatchWeather();
	}

	private void InitializeWeatherTexture() {
		weatherRenderTexture =
			new RenderTexture(512, 512, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
		weatherRenderTexture.enableRandomWrite = true;
		weatherRenderTexture.wrapMode          = TextureWrapMode.Repeat;
		weatherRenderTexture.Create();
		noiseShader.SetTexture(2, WeatherMap, weatherRenderTexture);
		Shader.SetGlobalTexture(WeatherTexture, weatherRenderTexture);
	}

	private void DispatchWeather() {
		int[] minMax = { int.MaxValue, int.MinValue };
		minMaxValues.SetData(minMax);

		noiseShader.SetBuffer(2, MinMaxBuffer, minMaxValues);
		
		weatherNoise.SetValues(noiseShader);
		
		noiseShader.Dispatch(2, weatherRenderTexture.width / 8, weatherRenderTexture.height / 8, 1);
		
		int[] readBuffer = new int[2];
		minMaxValues.GetData(readBuffer);
		Debug.Log(readBuffer[0] / 10000.0f + " " + readBuffer[1] / 10000.0f);
	}

	#endregion

	#endregion

	#region public functions

	public Texture GetTexture(int id) {
		return id switch {
			0 => perlinRenderTexture,
			1 => worleyRenderTexture,
			2 => weatherRenderTexture,
			3 => shadowRT,
			_ => perlinRenderTexture
		};
	}

	#endregion
}