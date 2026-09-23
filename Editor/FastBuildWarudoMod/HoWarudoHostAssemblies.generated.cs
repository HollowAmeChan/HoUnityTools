// HoWarudoHostAssemblies.generated.cs
//
// 【自动生成，不要手改】
//
// Warudo 宿主**实际自带**的运行时程序集名单，用来判断一个 Prefab 组件该不该把源码
// 复制进 Mod：宿主已经有了就别复制（复制多此一举，还容易编不过）。
//
// 生成方式（Warudo 0.15.0 实测）：
//   1) 列 <游戏>/Warudo_Data/Managed/*.dll —— 共 400 个、80 MB
//   2) 去掉扩展名
//   3) 排除 .NET 框架家族：mscorlib / netstandard / System* / Microsoft* / I18N*
//      WindowsBase / SMDiagnostics / CustomMarshalers / Accessibility / cscompmgd
//   4) 排除 Assembly-CSharp* 家族（用户自己的脚本就编译进这里，绝不能算宿主自带）
//   5) 排序去重
//
// 为什么不用别的判据，见 HoFastBuildWarudoModWindow.HostAssemblies.cs 的说明。
// Warudo 升级后想更新，重跑上面 5 步即可。

namespace Hollow.HoUnityTools.Editor.Warudo
{
    internal static class HoWarudoHostAssemblies
    {
        /// <summary>宿主自带（Warudo 0.15.0 实测）。329 个。</summary>
        internal static readonly string[] Names =
        {
            "_AVProVideo.Extensions.VideoPlayer", "ALINE", "AmazingAssets.ResizePro", "Animancer",
            "Animancer.Examples", "Animancer.FSM", "AssetsTools.NET", "AsyncIO",
            "AudioStream", "AudioStreamSupport", "Autodesk.Fbx", "AVProVideo.Extensions.Timeline",
            "AVProVideo.Extensions.UnityUI", "AVProVideo.Extensions.VisualEffectGraph", "AVProVideo.Runtime", "AYellowpaper.SerializedCollections",
            "BakeryRuntimeAssembly", "Beautify", "BezierSolution.Runtime", "Boxophobic.AtmosphericHeightFog.Runtime",
            "Boxophobic.Utils.Scripts", "Cinemachine", "CodeStage.AFPSCounter.Examples", "CodeStage.AFPSCounter.Runtime",
            "com.rlabrecque.steamworks.net", "com.sony.mocopi.receiver", "com.thenakeddev.dlss.Runtime", "com.thenakeddev.dlss.Runtime.URP",
            "Concentus", "Concentus.Oggfile", "CSCore", "DemiLib",
            "DeviceId", "DeviceId.Windows", "DOTween", "DOTween.Modules",
            "DOTweenPro", "DOTweenPro.Scripts", "DynamicBone", "EmbedIO",
            "endel.nativewebsocket", "extOSC", "FastSpringBone10", "FbxBuildTestAssets",
            "FMODUnity", "FMODUnityResonance", "HBAO.Demo.Runtime", "HBAO.Runtime",
            "Heathen.Core", "Heathen.Steamworks", "iMobileDevice-net", "io.sentry.unity.runtime",
            "Klak.Ndi.Runtime", "Klak.Spout.Runtime", "Lasp.Runtime", "Lattice.Runtime",
            "LeapMotion.LeapCSharp", "LottiePluginImpl", "MagicaCloth", "MagicaClothV2",
            "MathNet.Numerics", "MessagePack", "MessagePack.Annotations", "Mono.CSharp",
            "Mono.Data.Sqlite", "Mono.Messaging", "Mono.Posix", "Mono.Security",
            "Mono.WebBrowser", "MToon", "NaCl", "NAudio",
            "NAudio.Core", "NAudio.Wasapi", "NAudio.WinMM", "NetMQ",
            "Newtonsoft.Json", "Newtonsoft.Json.UnityConverters", "NiloToon.NiloToonURP.Runtime", "NiloToon.NiloToonURP.ShaderLibrary",
            "NiloToon.NiloToonURP.Shaders", "NLayer", "Novell.Directory.Ldap", "OggVorbisEncoder",
            "PlanarReflections5", "Popcron.Gizmos", "PotaToon.Runtime", "PusherClient",
            "RealisticEyeMovements", "Rokoko", "RootMotion", "RoslynCSharp",
            "RoslynCSharp.Compiler", "RtMidi.Runtime", "RuntimeSceneGizmo.Runtime", "Scripts",
            "Sentry", "Sentry.Microsoft.Bcl.AsyncInterfaces", "Sentry.System.Buffers", "Sentry.System.Collections.Immutable",
            "Sentry.System.Memory", "Sentry.System.Numerics.Vectors", "Sentry.System.Reflection.Metadata", "Sentry.System.Runtime.CompilerServices.Unsafe",
            "Sentry.System.Text.Encodings.Web", "Sentry.System.Text.Json", "Sentry.System.Threading.Tasks.Extensions", "Sentry.Unity",
            "Sentry.Unity.iOS", "Sentry.Unity.Native", "SharpHook", "SingularityGroup.HotReload.Runtime",
            "SingularityGroup.HotReload.Runtime.Public", "Sirenix.OdinInspector.Attributes", "Sirenix.OdinInspector.CompatibilityLayer", "Sirenix.Serialization",
            "Sirenix.Serialization.Config", "Sirenix.Utilities", "SoundIO.Runtime", "SPCRJointDynamics",
            "SpringBoneJobs", "SteamVR", "SteamVR_Actions", "StepIKClientDll",
            "StepIKforUnity", "StepMocapUDPCOMMON", "StepVRCSharp", "StepVRSDKUnity",
            "SuperSocket.ClientEngine", "Swan.Lite", "TagLibSharp", "Tools",
            "TransformGizmo", "Trivial.CodeSecurity", "Trivial.Mono.Cecil", "Trivial.Mono.Cecil.Mdb",
            "Trivial.Mono.Cecil.Pdb", "TwitchLib.Api", "TwitchLib.Api.Core", "TwitchLib.Api.Core.Enums",
            "TwitchLib.Api.Core.Interfaces", "TwitchLib.Api.Core.Models", "TwitchLib.Api.Helix", "TwitchLib.Api.Helix.Models",
            "TwitchLib.Client", "TwitchLib.Client.Enums", "TwitchLib.Client.Models", "TwitchLib.Communication",
            "TwitchLib.PubSub", "U3dCyanMocp", "uDesktopDuplication.Runtime", "uHomography.Runtime",
            "ULAR", "uLipSync.Runtime", "Ultraleap.Tracking.Core", "Ultraleap.Tracking.Hands",
            "Ultraleap.Tracking.InteractionEngine", "UMod", "UMod-Interface", "UMod-ModTools",
            "UMod-RoslynCSharp", "UMod-Shared", "UniGLTF", "UniGLTF.UniUnlit",
            "UniGLTF.Utils", "UniHumanoid", "UniRx", "UniTask",
            "UniTask.Addressables", "UniTask.DOTween", "UniTask.Linq", "UniTask.TextMeshPro",
            "Unity.AI.Navigation", "Unity.Burst", "Unity.Burst.Unsafe", "Unity.Collections",
            "Unity.Collections.LowLevel.ILSupport", "Unity.Deformations", "Unity.Entities", "Unity.Entities.Hybrid",
            "Unity.Entities.Hybrid.HybridComponents", "Unity.Formats.Fbx.Runtime", "Unity.InputSystem", "Unity.InputSystem.ForUI",
            "Unity.InternalAPIEngineBridge.002", "Unity.InternalAPIEngineBridge.012", "Unity.Jobs", "Unity.Mathematics",
            "Unity.Mathematics.Extensions", "Unity.Mathematics.Extensions.Hybrid", "Unity.MemoryProfiler", "Unity.Platforms.Common",
            "Unity.PlayableGraphVisualizer", "Unity.Postprocessing.Runtime", "Unity.Profiling.Core", "Unity.Properties",
            "Unity.Properties.Reflection", "Unity.Properties.UI", "Unity.RenderPipeline.Universal.ShaderLibrary", "Unity.RenderPipelines.Core.Runtime",
            "Unity.RenderPipelines.Core.ShaderLibrary", "Unity.RenderPipelines.ShaderGraph.ShaderGraphLibrary", "Unity.RenderPipelines.Universal.Runtime", "Unity.RenderPipelines.Universal.Shaders",
            "Unity.Scenes", "Unity.ScriptableBuildPipeline", "Unity.Serialization", "Unity.Services.Analytics",
            "Unity.Services.Core", "Unity.Services.Core.Analytics", "Unity.Services.Core.Configuration", "Unity.Services.Core.Device",
            "Unity.Services.Core.Environments", "Unity.Services.Core.Environments.Internal", "Unity.Services.Core.Internal", "Unity.Services.Core.Networking",
            "Unity.Services.Core.Registration", "Unity.Services.Core.Scheduler", "Unity.Services.Core.Telemetry", "Unity.Services.Core.Threading",
            "Unity.TerrainTools", "Unity.TextMeshPro", "Unity.Timeline", "Unity.Transforms",
            "Unity.Transforms.Hybrid", "unity.webp", "Unity.WebRTC", "Unity.XR.Management",
            "Unity.XR.OpenVR", "UnityEngine", "UnityEngine.AccessibilityModule", "UnityEngine.AIModule",
            "UnityEngine.AndroidJNIModule", "UnityEngine.AnimationModule", "UnityEngine.ARModule", "UnityEngine.AssetBundleModule",
            "UnityEngine.AudioModule", "UnityEngine.ClothModule", "UnityEngine.ClusterInputModule", "UnityEngine.ClusterRendererModule",
            "UnityEngine.CoreModule", "UnityEngine.CrashReportingModule", "UnityEngine.DirectorModule", "UnityEngine.DSPGraphModule",
            "UnityEngine.GameCenterModule", "UnityEngine.GIModule", "UnityEngine.GridModule", "UnityEngine.HotReloadModule",
            "UnityEngine.ImageConversionModule", "UnityEngine.IMGUIModule", "UnityEngine.InputLegacyModule", "UnityEngine.InputModule",
            "UnityEngine.JSONSerializeModule", "UnityEngine.LocalizationModule", "UnityEngine.NVIDIAModule", "UnityEngine.ParticleSystemModule",
            "UnityEngine.PerformanceReportingModule", "UnityEngine.Physics2DModule", "UnityEngine.PhysicsModule", "UnityEngine.ProfilerModule",
            "UnityEngine.RuntimeInitializeOnLoadManagerInitializerModule", "UnityEngine.ScreenCaptureModule", "UnityEngine.SharedInternalsModule", "UnityEngine.SpatialTracking",
            "UnityEngine.SpriteMaskModule", "UnityEngine.SpriteShapeModule", "UnityEngine.StreamingModule", "UnityEngine.SubstanceModule",
            "UnityEngine.SubsystemsModule", "UnityEngine.TerrainModule", "UnityEngine.TerrainPhysicsModule", "UnityEngine.TextCoreFontEngineModule",
            "UnityEngine.TextCoreTextEngineModule", "UnityEngine.TextRenderingModule", "UnityEngine.TilemapModule", "UnityEngine.TLSModule",
            "UnityEngine.UI", "UnityEngine.UIElementsModule", "UnityEngine.UIElementsNativeModule", "UnityEngine.UIModule",
            "UnityEngine.UmbraModule", "UnityEngine.UNETModule", "UnityEngine.UnityAnalyticsCommonModule", "UnityEngine.UnityAnalyticsModule",
            "UnityEngine.UnityConnectModule", "UnityEngine.UnityCurlModule", "UnityEngine.UnityTestProtocolModule", "UnityEngine.UnityWebRequestAssetBundleModule",
            "UnityEngine.UnityWebRequestAudioModule", "UnityEngine.UnityWebRequestModule", "UnityEngine.UnityWebRequestTextureModule", "UnityEngine.UnityWebRequestWWWModule",
            "UnityEngine.VehiclesModule", "UnityEngine.VFXModule", "UnityEngine.VideoModule", "UnityEngine.VirtualTexturingModule",
            "UnityEngine.VRModule", "UnityEngine.WindModule", "UnityEngine.XR.LegacyInputHelpers", "UnityEngine.XRModule",
            "UnityGifDecoder", "uOSC.Runtime", "uREPL.Runtime", "URPGrabPass",
            "uWindowCapture.Runtime", "Valve.Newtonsoft.Json", "Vexe.Fast.Reflection", "ViconPluginUtils",
            "VRC.Dynamics", "VRC.SDK3.Dynamics.PhysBone", "VRCSDK3A", "VRCSDKBase",
            "VRM", "VRM.QuickMetaLoader", "VRM10", "VRM10.MToon10",
            "VrmLib", "Vuplex.WebView", "Vuplex.WebView.Standalone", "VYaml",
            "VYaml.Annotations", "Warudo.Core", "Warudo.Plugins.Core", "WebSocket4Net",
            "websocket-sharp", "YamlDotNet", "ZibraAILiquid", "ZibraAILiquid.Samples",
            "ZString",
        };
    }
}
