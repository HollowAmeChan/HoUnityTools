# VRChat 上传失败：组件不在白名单里

## 症状

VRChat SDK 面板 Build & Publish 失败，Console 里出现：

```
Encountered the following validation issues during build:
The following component types are found on the Avatar and will be removed by the client:
    HoBoneRenderer, HoImportedConstraintMarker
Avatar validation failed
Failed to build avatar!
```

看着像网络问题，其实构建在**本地校验**阶段就中止了，根本没到联网那一步。

同一份日志里下面这些**都不致命**，别被带偏：

| 日志 | 性质 |
| --- | --- |
| `Controller VRCFury XXX was changed outside of NDMF animator services; cloning a second time` | NDMF 的 `Debug.Log`，提示信息 |
| `Import Error Code:(4) … lil_common_input_opt.hlsl … modification time …` | lilToon 构建时重写自己的 shader include，时间戳对不上 |
| `Loaded data for an avatar we do not own, clearing blueprint ID` | blueprintId 属于别人（复制来的模型），SDK 清掉后这次会新建 avatar 而不是覆盖 |

## 原因

VRChat 的 avatar 校验是**组件类型白名单制**，不是黑名单 —— 类型不在名单上就报错，**跟它重不重要无关**。

名单写死在 SDK 里：
`com.vrchat.base/Runtime/VRCSDK/Dependencies/VRChat/Scripts/Validation/AvatarValidation.cs`
的 `ComponentTypeWhiteListCommon` / `ComponentTypeWhiteListSdk3`。里面只有：

* Unity 内置：`Transform` / `Animator` / `SkinnedMeshRenderer` / 各种 Collider /
  `UnityEngine.Animations.*Constraint` / `Light` / `Camera` / `AudioSource` …
* FinalIK 全家
* VRC SDK 自己的：`VRCAvatarDescriptor` / `VRCPhysBone` / `VRCPhysBoneCollider` / `VRC*Constraint` / `VRCContact*` …

**没有 Modular Avatar、没有 VRCFury、没有 lilToon，也没有 HoTools。**

那 MA / VRCFury 挂着为什么能上传？因为它们**在预处理阶段就把自己删干净了**：

1. `VRCBuildPipelineCallbacks.OnPreprocessAvatar` 依次调用所有注册的钩子；
   `BuildFrameworkOptimizeHook.OnPreprocessAvatar`（NDMF 构建，MA / VRCFury 都在这一层）里，
   它们的组件是**构建期标签**，用完即 `DestroyImmediate`
   （MA 的 `BoneProxyProcessor` 最后一句就是 `Object.DestroyImmediate(proxy.Proxy)`）。
2. **之后** VRCSDK 才跑 `AvatarValidation` 查白名单 ⇒ 那时候它们已经不存在了。

HoTools 的组件是**作者工具**，没有任何钩子会删它们 ⇒ 一路活到校验 ⇒ 报错。

两个常见的错误尝试：

* **禁用组件没用**：校验查类型、不查 `m_Enabled`。
  `HoBoneRenderer` 本来就是 `m_Enabled: 0` 关着的，照样被列出来。
* **打 EditorOnly 标签也没用**：校验里确实是
  `FindIllegalComponents(..., excludeEditorOnly: true)`，但打 EditorOnly 标签的**整个物体会在构建时被剥掉** ——
  这 42 个标记所在的物体上还挂着要上传的 VRC 约束，打上去等于把约束一起删了。
  （正路是下面说的 **`IEditorOnly` 接口**：它只删组件，不动物体。）

## 怎么办

**两个组件实现 `IHoEditorOnly`**（`Runtime/HoEditorOnly.cs`）—— 这就够了，不需要任何构建钩子：

```csharp
public interface IHoEditorOnly
#if HO_VRCSDK
    : IEditorOnly          // = VRC.SDKBase.IEditorOnly
#endif
{ }
```

套路抄的是 NDMF 的 `INDMFEditorOnly`：装了 VRChat SDK 时它就是真正的 `VRC.SDKBase.IEditorOnly`，
否则是个空接口 —— 组件只写一个接口名，不用到处 `#if`，HoTools 在纯 Unity 工程里照样编译。
`HO_VRCSDK` 由 `HoUnityTools.Runtime.asmdef` 的 `versionDefines` 按 `com.vrchat.base` 定义
（`overrideReferences: false`，所以 `VRCSDKBase.dll` 在装了 SDK 的工程里本来就被自动引用）。

用它是为了吃到 VRChat「EditorOnly」这一条豁免：

| | 效果 | 依据 |
| --- | --- | --- |
| **面板不标红** | 校验走 `FindIllegalComponents(..., excludeEditorOnly: true)`，实现 `IEditorOnly` 的组件被跳过 | MA / VRCFury 的标签组件走的同一条路（`AvatarTagComponent : AvatarTagComponent, INDMFEditorOnly`，而 `INDMFEditorOnly : IEditorOnly`），所以它们从来不被标红 |
| **构建时自动消失** | VRCSDK 的 `RemoveAvatarEditorOnly` 回调（order -1024）会摘掉 EditorOnly 物体和 `IEditorOnly` 组件 | VRCFury `KeepEditorOnlyComponentsLongerHook` 的注释：「The VRCSDK removes all EditorOnly objects and components at -1024 by default」 |

⚠️ **只删组件、不删物体** —— `HoImportedConstraintMarker.hostRole == VrcConstraintObject(2)` 时，
它所在的物体就是承载 VRC 约束的空物体，连着删等于把用户配好的约束也删了。`IEditorOnly` 的语义正好是只删组件。

**以后再加「只给编辑器用、不该上传」的组件**：让它实现 `IHoEditorOnly` 就行。

### 另有一个冗余的保险：`Editor/VrcBuild/`

那是最早的方案（一个 NDMF pass，只在构建时删组件），**治不了面板红框** ——
因为面板的 `CreateValidationsGUI` 每次都拿**场景里的 avatar** 跑 `OnGUIAvatarCheck`，
而 NDMF pass 只作用在 SDK 克隆出来的构建副本上。

`IHoEditorOnly` 上来之后它已经被完全覆盖，属于**冗余**。先留着当保险，
确认上传正常后可以整份删掉（删了就少一个对 NDMF 的依赖）。

## 验证

1. 选中 avatar，SDK 面板的校验列表里**不应该再出现** `HoBoneRenderer` / `HoImportedConstraintMarker`。
2. 场景 / 预制件里这两个组件**依然在**，骨骼渲染与约束面板照常用。
3. 上传成功后 Console 里会有 `[HoUnityTools] VRC 构建：已从构建副本移除 43 个作者工具组件。`
   —— 那是上面那个保险 pass 打的；把 `Editor/VrcBuild/` 删掉后这行就没了。

