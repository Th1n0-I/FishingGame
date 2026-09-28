using System;
using UnityEditor;
using UnityEditor.Build.Reporting;

// Called by .github/workflows/build.yml. The build settings have no scenes in them, so the scene is picked here.
public static class CIBuild {
	private static readonly string[] Scenes = { "Assets/Scenes/NewTHINODEVSCEN.unity" };

	public static void BuildWindows() {
		// GameCI passes the output path as -customBuildPath <path>.
		string[] args  = Environment.GetCommandLineArgs();
		int      index = Array.IndexOf(args, "-customBuildPath");
		string   path  = index >= 0 && index + 1 < args.Length ? args[index + 1] : "Build/Windows/FishingGame.exe";

		BuildReport report = BuildPipeline.BuildPlayer(Scenes, path, BuildTarget.StandaloneWindows64, BuildOptions.None);
		if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
	}
}
