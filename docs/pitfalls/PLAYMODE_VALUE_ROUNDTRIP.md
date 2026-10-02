# 播放模式调参带不回来 / 带回来时引用变空

## 症状

在 Play 模式里调 PhysBone，退出后改的值全没了；
或者用了第三方工具把它带回来了，但组件上的 **Root Transform / Colliders / Ignore Transforms
变成了 None**，prefab 实例看着像被弄坏了。

## 原因

**为什么值会丢**：Play 模式里对场景的改动，退出时会被还原 —— 这是 Unity 的设计，
不是 bug。想把改动留下来，必须自己在退出前抓一份快照。

**为什么"抓回来"反而会把引用弄空**：`jp.lilxyzw.editortoolbox` 的 `PlayModeSaver`
（组件菜单里的 `Save changes in PlayMode`）是这么做的：

1. **记录**：`EditorJsonUtility.ToJson(组件)` 把**整个组件**序列化成 JSON；
2. **回写**：`Instantiate` 一个临时克隆 → `EditorJsonUtility.FromJsonOverwrite(json, 克隆)`
   → `CopyProperties(克隆, 原组件)` 逐条抄回原组件。

两个致命点：

* **引用是以 fileID 存的，而 fileID 只在它所属的序列化上下文里有效。**
  `FromJsonOverwrite` 又是在一个 `Instantiate` 出来的**临时克隆**上还原的 ——
  当 avatar 是 prefab 实例、骨骼都在 prefab 里时，这些引用在那个上下文里解析不回来，
  就写成空了。Unity 自己也有 issue 记着同一类问题：
  [Scriptable Object references are lost when overwritten by FromJsonOverwrite](https://issuetracker-mig.prd.it.unity3d.com/issues/scriptable-object-references-are-lost-when-overwritten-by-fromjsonoverwrite)。
* 它的 `CopyProperties` **逐条复制所有序列化属性**，只排除了 6 个 prefab / 内部字段
  （`m_ObjectHideFlags` · `m_CorrespondingSourceObject` · `m_PrefabInstance` ·
  `m_PrefabAsset` · `m_GameObject` · `m_EditorHideFlags`），
  所以 `rootTransform` / `ignoreTransforms` / `colliders` / 各种 filter **全在复制之列**。

## 怎么办

用 **`Editor/PhysBonePlayMode/` 的 `HoPhysBonePlayModeSaver`**。与上面那套的差别只有一句话：
**只抄数值，引用一律不碰。**

* **入口**：Play 模式里在 **PhysBone / PhysBoneCollider** 的组件菜单（⚙）点
  `HoUnityTools/保存播放模式改动`；也可以选中若干物体走
  `GameObject/HoUnityTools/保存播放模式改动（选中）`（万一 CONTEXT 那条因为类型名解析不出来
  没出现，就用这个兜底入口）。
* **回写**：退出 Play 模式时按「场景层级路径 + 组件类型 + 同类序号」找回同一个组件
  （Player 模式下场景是只读的，所以路径不会变），只把**数值字段**抄回去 ——
  判据是 `SerializedPropertyType` 白名单，凡是 `ObjectReference`、以及元素是 `PPtr` 的数组，
  一律跳过。
* **prefab 安全**：走 `SerializedObject` + `ApplyModifiedProperties()`，所以在 prefab 实例上会
  正确落成 prefab override，组件与 prefab 的关联不会断。
* **不会丢**：记录存在 `SessionState` 里，所以播放模式进出即使触发域重载也还在。

**为什么可以放心跳过引用**：PhysBone 调参本来就不改引用 ——
pull / spring / stiffness / gravity / radius / 限位 / 各种曲线全是数值，
Root Transform、Colliders、Ignore Transforms、filter 这些从调参的第一天起就没被动过。

## 注意

用这个工具之前**先把 Editor Toolbox 的 PlayModeSaver 关掉**
（Editor Toolbox 的设置窗口里可以逐项开关功能），
否则两个都会去回写，而那个会把引用弄空。
