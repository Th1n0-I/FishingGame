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

	// Every kernel that samples the cloud density.
	private static readonly int[] DensityKernels =
		{ NoiseController.KernelMain, NoiseController.KernelShadows, NoiseController.KernelLightVolume };

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
		foreach (int kernel in DensityKernels) {
			cs.SetTexture(kernel, shapeCurveID,   shapeLut);
			cs.SetTexture(kernel, densityCurveID, densityLut);
		}
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
	// Starts at 0 so scenes that saved the old bool (false) load as Off.
	private enum UpscaleMode { Off, TwoByTwo, FourByFour }

	private enum CloudQuality { Custom, Low, Medium, High, Ultra }

	// What a quality preset sets.
	private struct QualityValues {
		public int         stepAmount, textureDivide;
		public UpscaleMode upscaling;
		public float       detailDistance;
	}

	private int UpscaleFactor => temporalUpscaling switch {
		UpscaleMode.TwoByTwo   => 2,
		UpscaleMode.FourByFour => 4,
		_                      => 1
	};

	[Header("Volumetrics")]
	[Header("-Quality")]
	[SerializeField, Tooltip("Anything but Custom sets Step Amount, Texture Divide, Temporal Upscaling and Detail Distance when the game starts. F1 to F4 pick Low to Ultra in game, F5 goes back to Custom.")]
	private CloudQuality quality = CloudQuality.High;
	[SerializeField, Tooltip("Takes bigger steps through empty space and only goes back to normal steps near clouds.")]
	private bool emptySpaceSkipping = true;
	[SerializeField, Tooltip("Bakes the far part of the light samples into a 3D texture around the camera every frame instead of sampling them for every step.")]
	private bool useLightVolume = true;
	[SerializeField, Tooltip("Distance in meters where the cloud detail has faded to its average, from 60% of it on. 0 keeps full detail everywhere.")]
	private float detailDistance = 35000f;
	[SerializeField]
	private int textureDivide = 2;
	[SerializeField] private float stepSize            = 1.0f;
	[SerializeField] private int   firstPassStepAmount = 1;
	[SerializeField] private int   stepAmount          = 1;
	[SerializeField] private float maxDist             = 100;
	[SerializeField] private bool  firstPass;
	[SerializeField] private bool  useStepSize;
	[SerializeField, Tooltip("Renders 1 of every 4 (2x2) or 16 (4x4) pixels per frame and builds the rest up over time. 2x2 copes a lot better with fast clouds.")]
	private UpscaleMode temporalUpscaling = UpscaleMode.Off;
	[SerializeField, Range(0.02f, 1), Tooltip("How much of the new frame goes into the result. Lower is smoother but reacts slower, 1 turns temporal accumulation off.")]
	private float temporalBlend = 0.1f;

	[Header("-Settings")]
	[SerializeField, Range(0, 1), Tooltip("How much of the sky the clouds fill. Seen from straight above 0.67 covers about 28%, 0.78 about 45%.")]
	private float cloudCoverage = 0.78f;
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

	[Header("-Wind")]
	[SerializeField, Tooltip("How fast the clouds drift in m/s. Real clouds move about 5 to 30 m/s.")]
	private float windSpeed = 40f;
	[SerializeField, Tooltip("Direction the clouds drift towards (x, z).")]
	private Vector2 windDirection = new Vector2(-1, 0);
	[SerializeField, Tooltip("How fast the detail noise rises through the clouds in m/s, so the edges churn a bit. The temporal reprojection can't follow this part, so keep it small.")]
	private float detailRiseSpeed = 5f;
	[SerializeField, Tooltip("How far in meters the cloud tops lean downwind.")]
	private float windShear = 1000f;

	[Header("-Cirrus")]
	[SerializeField, Tooltip("Thin streaky ice clouds high above the others.")]
	private bool cirrus = true;
	[SerializeField] private float cirrusHeight = 10000f;
	[SerializeField, Range(0, 1)] private float cirrusCoverage = 0.65f;
	[SerializeField, Range(0, 1)] private float cirrusOpacity  = 0.55f;
	[SerializeField, Tooltip("Size of the streaks along the wind and across it, in meters.")]
	private Vector2 cirrusStreakSize = new Vector2(60000f, 6000f);
	[SerializeField, Tooltip("Size of the patches the cirrus comes in, in meters.")]
	private float cirrusPatchSize = 100000f;

	[Header("-Day Night Cycle")]
	[SerializeField, Tooltip("Drives the sun, moon and cloud lighting. When empty the one in the scene is used, or one gets created.")]
	private DayNightCycle dayNightCycle;
	[SerializeField, Tooltip("Create a Day Night Cycle with the default settings when the scene doesn't have one.")]
	private bool createDayNightCycle = true;

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
	private          RenderTexture   currentRT, currentDepthRT;
	private          int             historyIndex, frameIndex;
	private          bool            historyValid, temporalEnabled = true;
	private          float           toggleMessageUntil;

	// How far the wind has carried the clouds and how far the detail has risen, in meters.
	private Vector3 windTravel;
	private float   detailRise;

	// The far part of the light cone around the camera, 375 m voxels over the cloud layer.
	private RenderTexture lightVolumeRT;
	private const int     LightVolumeWidth = 128, LightVolumeHeight = 32;
	private const float   LightVolumeSize  = 48000f;

	// The inspector values, for going back to Custom, and which preset is applied.
	private QualityValues customQuality;
	private CloudQuality  appliedQuality;
	private bool          detailFade = true;

	// Kernel order in VolumetricCompute.compute.
	internal const int KernelMain = 0, KernelResolve = 1, KernelShadows = 2, KernelLightVolume = 3;


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
	private static readonly int WindTravel                  = Shader.PropertyToID("wind_travel");
	private static readonly int WindDirectionID             = Shader.PropertyToID("wind_direction");
	private static readonly int ShadowStepSize              = Shader.PropertyToID("shadow_step_size");
	private static readonly int ShadowConeSpread            = Shader.PropertyToID("shadow_cone_spread");
	private static readonly int LightContributionSunset     = Shader.PropertyToID("light_contribution_sunset");
	private static readonly int FogBaseColorNight           = Shader.PropertyToID("fog_base_color_night");
	private static readonly int PixelOffset                 = Shader.PropertyToID("pixel_offset");
	private static readonly int UpscaleFactorID             = Shader.PropertyToID("upscale_factor");
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
	private static readonly int BackScattering              = Shader.PropertyToID("back_scattering");
	private static readonly int BackScatteringWeight        = Shader.PropertyToID("back_scattering_weight");
	private static readonly int AmbientBottom               = Shader.PropertyToID("ambient_bottom");
	private static readonly int PowderStrength              = Shader.PropertyToID("powder_strength");
	private static readonly int HorizonFade                 = Shader.PropertyToID("horizon_fade");
	private static readonly int WindShear                   = Shader.PropertyToID("wind_shear");
	private static readonly int CurrentFrame                = Shader.PropertyToID("current_frame");
	private static readonly int CurrentDepth                = Shader.PropertyToID("current_depth");
	private static readonly int ResultDepth                 = Shader.PropertyToID("ResultDepth");
	private static readonly int CloudSize                   = Shader.PropertyToID("cloud_size");
	private static readonly int FrameIndex                  = Shader.PropertyToID("frame_index");
	private static readonly int HistoryValid                = Shader.PropertyToID("history_valid");
	private static readonly int TemporalBlend               = Shader.PropertyToID("temporal_blend");
	private static readonly int WindOffset                  = Shader.PropertyToID("wind_offset");
	private static readonly int ShadowCenter                = Shader.PropertyToID("shadow_center");
	private static readonly int CloudShadowCenter           = Shader.PropertyToID("_CloudShadowCenter");
	private static readonly int CloudShadowLight            = Shader.PropertyToID("_CloudShadowLight");
	private static readonly int SkyDepth                    = Shader.PropertyToID("sky_depth");
	private static readonly int EmptySpaceSkipping          = Shader.PropertyToID("empty_space_skipping");
	private static readonly int LodFade                     = Shader.PropertyToID("lod_fade");
	private static readonly int UseLightVolumeID            = Shader.PropertyToID("use_light_volume");
	private static readonly int LightVolume                 = Shader.PropertyToID("light_volume");
	private static readonly int LightVolumeOut              = Shader.PropertyToID("light_volume_out");
	private static readonly int LightVolumeOrigin           = Shader.PropertyToID("light_volume_origin");
	private static readonly int LightVolumeSizeID           = Shader.PropertyToID("light_volume_size");
	private static readonly int LightVolumeRes              = Shader.PropertyToID("light_volume_res");
	private static readonly int CirrusParams                = Shader.PropertyToID("cirrus_params");
	private static readonly int CirrusScale                 = Shader.PropertyToID("cirrus_scale");
	private static readonly int PixelAngle                  = Shader.PropertyToID("pixel_angle");

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

		if (!dayNightCycle) dayNightCycle = FindAnyObjectByType<DayNightCycle>();
		if (!dayNightCycle && createDayNightCycle) dayNightCycle = new GameObject("Day Night Cycle").AddComponent<DayNightCycle>();

		customQuality = CurrentQualityValues();
		ApplyQuality(quality);
		// Show the controls for a bit at the start.
		toggleMessageUntil = UnityEngine.Time.unscaledTime + 6;
	}

	private void OnValidate() {
		if (cloudTypes == null) return;
		foreach (var v in cloudTypes) v?.MakeCurvesDirty();
	}

	private void Update() {
		CheckToggleKeys();
		// Also picks up the preset being changed in the inspector while playing.
		if (quality != appliedQuality) ApplyQuality(quality);

		if (!regenerateNoise && !constantlyGenerateNoise) return;
		DispatchWeather();
		DispatchNoise();
		regenerateNoise = false;
	}

	private void OnDestroy() {
		ReleaseCloudTextures();
		foreach (var rt in new[] { perlinRenderTexture, worleyRenderTexture, weatherRenderTexture, shadowRT, lightVolumeRT }) DestroyTexture(rt);
		if (cloudTypes != null) foreach (var v in cloudTypes) v?.Release();
		if (CumulusLut) Destroy(CumulusLut);
		minMaxValues?.Release();
	}

	// Keys to switch the presets and every feature in a build, so they can be compared without the editor.
	private void CheckToggleKeys() {
		var keyboard = Keyboard.current;
		if (keyboard == null) return;

		bool pressed = true;
		if (keyboard.f1Key.wasPressedThisFrame) quality = CloudQuality.Low;
		else if (keyboard.f2Key.wasPressedThisFrame) quality = CloudQuality.Medium;
		else if (keyboard.f3Key.wasPressedThisFrame) quality = CloudQuality.High;
		else if (keyboard.f4Key.wasPressedThisFrame) quality = CloudQuality.Ultra;
		else if (keyboard.f5Key.wasPressedThisFrame) quality = CloudQuality.Custom;
		else if (keyboard.tKey.wasPressedThisFrame) temporalEnabled = !temporalEnabled;
		else if (keyboard.uKey.wasPressedThisFrame) temporalUpscaling = (UpscaleMode)(((int)temporalUpscaling + 1) % 3);
		else if (keyboard.kKey.wasPressedThisFrame) emptySpaceSkipping = !emptySpaceSkipping;
		else if (keyboard.lKey.wasPressedThisFrame) useLightVolume = !useLightVolume;
		else if (keyboard.oKey.wasPressedThisFrame) detailFade = !detailFade;
		else if (keyboard.hKey.wasPressedThisFrame) cirrus = !cirrus;
		else pressed = false;
		if (pressed) toggleMessageUntil = UnityEngine.Time.unscaledTime + 3;
	}

	private void OnGUI() {
		if (UnityEngine.Time.unscaledTime > toggleMessageUntil) return;
		string upscaling = UpscaleFactor > 1 ? UpscaleFactor + "x" + UpscaleFactor : "off";
		GUI.Label(new Rect(10, Screen.height - 55, 1000, 25),
		          $"[F1-F4] Quality: {quality} ({Mathf.Max(stepAmount, 1)} steps)   [F5] Custom    [T] Temporal accumulation: {OnOff(temporalEnabled)}    [U] Temporal upscaling: {upscaling}");
		GUI.Label(new Rect(10, Screen.height - 30, 1000, 25),
		          $"[K] Empty space skipping: {OnOff(emptySpaceSkipping)}    [L] Light volume: {OnOff(useLightVolume)}    [O] Distance detail fade: {OnOff(detailFade && detailDistance > 0)}    [H] Cirrus: {OnOff(cirrus)}");
	}

	private static string OnOff(bool on) => on ? "on" : "off";

	private QualityValues CurrentQualityValues() => new QualityValues {
		stepAmount = stepAmount, textureDivide = textureDivide, upscaling = temporalUpscaling, detailDistance = detailDistance
	};

	// Sets the preset's values, Custom goes back to the inspector values.
	private void ApplyQuality(CloudQuality preset) {
		// Remember what Custom was set to (including changes made while on it) when leaving it.
		if (appliedQuality == CloudQuality.Custom && preset != CloudQuality.Custom) customQuality = CurrentQualityValues();
		var values = preset switch {
			CloudQuality.Low    => new QualityValues { stepAmount = 64,  textureDivide = 2, upscaling = UpscaleMode.FourByFour, detailDistance = 15000f },
			CloudQuality.Medium => new QualityValues { stepAmount = 96,  textureDivide = 2, upscaling = UpscaleMode.TwoByTwo,   detailDistance = 25000f },
			CloudQuality.High   => new QualityValues { stepAmount = 120, textureDivide = 2, upscaling = UpscaleMode.TwoByTwo,   detailDistance = 35000f },
			CloudQuality.Ultra  => new QualityValues { stepAmount = 160, textureDivide = 2, upscaling = UpscaleMode.Off,        detailDistance = 0f },
			_                   => customQuality
		};
		stepAmount        = values.stepAmount;
		textureDivide     = values.textureDivide;
		temporalUpscaling = values.upscaling;
		detailDistance    = values.detailDistance;
		appliedQuality    = preset;
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
		CreateLightVolume();

		foreach (int kernel in new[] { KernelMain, KernelShadows, KernelLightVolume }) {
			volumetricsShader.SetTexture(kernel, WeatherMap, weatherRenderTexture);
			volumetricsShader.SetTexture(kernel, PerlinTex1, perlinRenderTexture);
			volumetricsShader.SetTexture(kernel, WorleyTex1, worleyRenderTexture);
			volumetricsShader.SetTexture(kernel, Lut,        CumulusLut);
		}
		volumetricsShader.SetTexture(KernelMain,        LightVolume,    lightVolumeRT);
		volumetricsShader.SetTexture(KernelLightVolume, LightVolumeOut, lightVolumeRT);
	}

	private void CreateLightVolume() {
		lightVolumeRT = new RenderTexture(LightVolumeWidth, LightVolumeHeight, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear) {
			dimension         = TextureDimension.Tex3D,
			volumeDepth       = LightVolumeWidth,
			enableRandomWrite = true,
			wrapMode          = TextureWrapMode.Clamp,
			filterMode        = FilterMode.Bilinear,
		};
		lightVolumeRT.Create();
	}

	// (Re)creates the cloud textures whenever the resolution or the temporal upscaling setting changes.
	private void EnsureCloudTextures(int screenWidth, int screenHeight) {
		int divide        = Mathf.Max(1, textureDivide);
		int width         = Mathf.Max(1, screenWidth  / divide);
		int height        = Mathf.Max(1, screenHeight / divide);
		int factor        = UpscaleFactor;
		int currentWidth  = (width  + factor - 1) / factor;
		int currentHeight = (height + factor - 1) / factor;

		if (currentRT && currentRT.width == currentWidth && currentRT.height == currentHeight && currentDepthRT &&
		    historyRTs[0] && historyRTs[0].width == width && historyRTs[0].height == height) return;

		ReleaseCloudTextures();
		currentRT      = CreateCloudTexture(currentWidth, currentHeight);
		// Full float, the cloud distance goes past what half precision can hold (65 km).
		currentDepthRT = CreateCloudTexture(currentWidth, currentHeight, RenderTextureFormat.RFloat);
		historyRTs[0]  = CreateCloudTexture(width, height);
		historyRTs[1]  = CreateCloudTexture(width, height);
		historyValid   = false;
	}

	private static RenderTexture CreateCloudTexture(int width, int height,
	                                                RenderTextureFormat format = RenderTextureFormat.ARGBHalf) {
		// Half precision is plenty for cloud colours and halves the memory traffic of ARGBFloat.
		var rt = new RenderTexture(width, height, 0, format) {
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
		DestroyTexture(currentDepthRT);
		DestroyTexture(historyRTs[0]);
		DestroyTexture(historyRTs[1]);
		currentRT      = null;
		currentDepthRT = null;
		historyRTs[0]  = historyRTs[1] = null;
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
			volumetricsShader.SetTexture(KernelMain,    ResultDepth,      currentDepthRT);
			volumetricsShader.SetTexture(KernelResolve, CurrentFrame,     currentRT);
			volumetricsShader.SetTexture(KernelResolve, CurrentDepth,     currentDepthRT);
			volumetricsShader.SetTexture(KernelResolve, RendertextureOld, historyRead);
			volumetricsShader.SetTexture(KernelResolve, Result,           historyWrite);
			volumetricsShader.SetTexture(KernelShadows, ShadowRT,         shadowRT);
			volumetricsShader.SetVector(CloudSize, new Vector4(historyWrite.width, historyWrite.height, currentRT.width, currentRT.height));

			// The jitter only moves when something averages it, otherwise it would crawl.
			float blend = temporalEnabled ? temporalBlend : 1.0f;
			frameIndex++;
			volumetricsShader.SetInt(FrameIndex, blend < 1.0f || UpscaleFactor > 1 ? frameIndex : 0);
			volumetricsShader.SetFloat(TemporalBlend, blend);
			volumetricsShader.SetBool(HistoryValid, historyValid);

			// One wind moves every noise layer, so the history can be moved back by exactly the same step.
			float   deltaTime     = UnityEngine.Time.deltaTime;
			Vector2 windDir       = windDirection.sqrMagnitude > 1e-6f ? windDirection.normalized : Vector2.left;
			var     windVelocity  = new Vector3(windDir.x, 0, windDir.y) * windSpeed;
			windTravel += windVelocity * deltaTime;
			detailRise += detailRiseSpeed * deltaTime;
			volumetricsShader.SetVector(WindTravel,      new Vector4(windTravel.x, windTravel.y, windTravel.z, detailRise));
			volumetricsShader.SetVector(WindDirectionID, new Vector4(windDir.x, windDir.y, 0, 0));
			volumetricsShader.SetVector(WindOffset,      -windVelocity * deltaTime);
			volumetricsShader.SetFloat(SkyDepth, SystemInfo.usesReversedZBuffer ? 0.0f : 1.0f);

			// With a day night cycle the clouds get its light (sun by day, moon by night) and its sky colour,
			// the colour already has the sunset tint in it. Without one the settings above are used.
			bool    cycle        = dayNightCycle && dayNightCycle.CloudLight;
			Vector3 lightForward = cycle ? dayNightCycle.CloudLight.forward : sun.transform.forward;

			// The shadow map holds the light through the clouds for rays starting at the cloud base, and the composite
			// follows the light from each ground pixel up to the cloud base to look it up. So the map goes around
			// where the light from the ground below the camera passes the cloud base (sideways by the base height
			// / tan(elevation)), snapped to whole texels so the ground shadows don't shimmer while the camera moves.
			var     camPosition  = cam.transform.position;
			float   shadowPlane  = bounds.bounds.min.y;
			Vector3 toLight      = -lightForward.normalized;
			var     shadowFocus  = new Vector2(camPosition.x, camPosition.z);
			if (toLight.y > 0.05f) shadowFocus += new Vector2(toLight.x, toLight.z) * (Mathf.Max(shadowPlane - Mathf.Min(camPosition.y, 0f), 0f) / toLight.y);
			float shadowTexel  = shadowWorldSize / shadowRT.width;
			var   shadowCenter = new Vector4(Mathf.Floor(shadowFocus.x / shadowTexel) * shadowTexel,
			                                 Mathf.Floor(shadowFocus.y / shadowTexel) * shadowTexel, 0, 0);
			volumetricsShader.SetVector(ShadowCenter, shadowCenter);
			Shader.SetGlobalVector(CloudShadowCenter, shadowCenter);
			Shader.SetGlobalVector(CloudShadowLight, new Vector4(toLight.x, toLight.y, toLight.z, shadowPlane));

			volumetricsShader.SetVector(CamPos,
			                            new Vector4(cam.transform.position.x, cam.transform.position.y,
			                                        cam.transform.position.z, 0.0f));
			volumetricsShader.SetVector(FogColor,          cycle ? dayNightCycle.CloudAmbient : fogColor);
			volumetricsShader.SetVector(FogBaseColorNight, cycle ? dayNightCycle.CloudAmbient : fogColorNight);

			volumetricsShader.SetVector(LightContribution,       lightContribution);
			volumetricsShader.SetVector(LightContributionSunset, cycle ? lightContribution : lightContributionSunset);

			volumetricsShader.SetVector(MainLightColor, cycle ? dayNightCycle.CloudLightColor : sun.color.linear);
			volumetricsShader.SetVector(LightDirection, lightForward);

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
			volumetricsShader.SetFloat(ShadowStepSize,    shadowStepSize);
			volumetricsShader.SetFloat(ShadowConeSpread,  shadowConeSpread);
			volumetricsShader.SetFloat(BackScattering,    backScattering);
			volumetricsShader.SetFloat(BackScatteringWeight, backScatteringWeight);
			volumetricsShader.SetFloat(AmbientBottom,     ambientBottom);
			volumetricsShader.SetFloat(PowderStrength,    powderStrength);
			volumetricsShader.SetFloat(HorizonFade,       horizonFade);
			volumetricsShader.SetFloat(WindShear,         windShear);
			volumetricsShader.SetFloat(Coverage,          cloudCoverage);

			volumetricsShader.SetBool(EmptySpaceSkipping, emptySpaceSkipping);
			// Detail amount = saturate(distance * x + y): full up to 60% of the detail distance, none at the distance.
			volumetricsShader.SetVector(LodFade, detailFade && detailDistance > 0
				                                     ? new Vector4(-1f / (0.4f * detailDistance), 2.5f, 0, 0)
				                                     : new Vector4(0, 1, 0, 0));
			volumetricsShader.SetVector(CirrusParams, new Vector4(cirrusHeight, cirrusCoverage, cirrusOpacity, cirrus ? 1 : 0));
			volumetricsShader.SetVector(CirrusScale,  new Vector4(Mathf.Max(cirrusStreakSize.x, 1), Mathf.Max(cirrusStreakSize.y, 1),
			                                                      Mathf.Max(cirrusPatchSize, 1), 0));
			volumetricsShader.SetFloat(PixelAngle, 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / historyWrite.height);
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
			volumetricsShader.SetInt(UpscaleFactorID, UpscaleFactor);

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

			// Rebuilt every frame (it costs about as much as a few rows of the raymarch), so it never lags behind the
			// wind or the sun. Snapped to whole voxels so the voxels stay put in the world while the camera moves.
			volumetricsShader.SetBool(UseLightVolumeID, useLightVolume);
			if (useLightVolume) {
				var   boxMin = bounds.bounds.min;
				var   boxMax = bounds.bounds.max;
				float voxel  = LightVolumeSize / LightVolumeWidth;
				// Spans the box height, shifted so a layer of voxel centres sits just above the cloud base. Otherwise
				// the filtering pulls in the clear air below and the cloud bottoms get too bright at low sun.
				float layer     = Mathf.Max(boxMax.y - boxMin.y, 1) / LightVolumeHeight;
				float cloudBase = cumulus.startAltitude + 1f;
				float bottom    = cloudBase - (Mathf.Floor((cloudBase - boxMin.y) / layer) + 0.5f) * layer;
				var   origin    = new Vector4(Mathf.Floor(camPosition.x / voxel) * voxel - LightVolumeSize * 0.5f, bottom,
				                              Mathf.Floor(camPosition.z / voxel) * voxel - LightVolumeSize * 0.5f, voxel);
				volumetricsShader.SetVector(LightVolumeOrigin, origin);
				volumetricsShader.SetVector(LightVolumeSizeID, new Vector4(LightVolumeSize, layer * LightVolumeHeight, LightVolumeSize, 0));
				volumetricsShader.SetVector(LightVolumeRes,    new Vector4(LightVolumeWidth, LightVolumeHeight, LightVolumeWidth, 0));
				volumetricsShader.Dispatch(KernelLightVolume, LightVolumeWidth / 4, LightVolumeHeight / 4, LightVolumeWidth / 4);
			}

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
		// The cirrus samples it at a distance, mips keep that from aliasing. Only mip 0 gets written by the kernel.
		weatherRenderTexture.useMipMap         = true;
		weatherRenderTexture.autoGenerateMips  = false;
		weatherRenderTexture.filterMode        = FilterMode.Trilinear;
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
		weatherRenderTexture.GenerateMips();

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