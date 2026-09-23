# Unity 资产与编辑器的坑

## 1. `AssetDatabase.CopyAsset` 会把 GUID 换掉

症状：装配完控制器，场景里引用过它的地方（窥视对象的 Animator）**全断了**。
原因：覆盖一个已存在的资产等于删掉再写，新文件拿到**新 GUID**。
怎么办：装配前记住旧 GUID，复制完把它写回新 `.meta` 的 `guid:` 行，再
`ImportAsset(ForceUpdate | ForceSynchronousImport)`。用例里有一条断言专门守这件事
（`覆盖式装配保留资产 GUID`）。

## 2. 改别人的 `.anim` = 改共享资产

症状：装配时"顺手"把片段重绑到角色网格，结果模板包里那份 `.anim` 被改了 —— 别的角色跟着变形。
原因：片段是**外部资产**时（`AssetDatabase.GetAssetPath(clip) != 我们这份文件`），
`AnimationUtility.SetEditorCurve` 直接写在别人身上。
怎么办：先 `Object.Instantiate` 复制一份 `AddObjectToAsset` 进自己的文件，再改它，
最后把树/状态里指向原片的引用全部换成副本（这就是 `ResolveClips → Retarget → Repoint` 的顺序）。

## 3. `HideAndDontSave` + `AddObjectToAsset` = Unity 断言

症状：每次装配都刷一条
`Assertion failed on expression: '!(o->TestHideFlag(Object::kDontSaveInEditor) && (options & kAllowDontSaveObjectsToBePersistent) == 0)'`。
原因：复制出来的片段带 `HideFlags.HideAndDontSave`（那是给**纯内存**预览副本用的），却被当成子资产落盘。
怎么办：落盘的副本不要设这个 flag；只有内存里的预览副本（`Compile` 那批）才设。

## 4. Runtime 组件里存不了 `DefaultAsset`

症状：想在组件上加一个"文件夹"字段，项目里拖不进去 / 编译不过。
原因：`DefaultAsset` 在 `UnityEditor` 命名空间下，Runtime 程序集引用不到。
怎么办：字段存**路径字符串**（`public string animationFolder;`），编辑器那边用一个
`EditorGUILayout.ObjectField(..., typeof(DefaultAsset), false)` 画选择器，选中后写回 `.path`。

## 5. 按名字对资产：文件名 ≠ `clip.name`

- `AssetDatabase.FindAssets("t:AnimationClip", new[] { folder })` 只给 GUID，要自己 `GUIDToAssetPath`。
- 槽位匹配用的是**文件名去扩展名**（`<键名>.anim`），同时把 `clip.name` 作为兜底键 ——
  两者可能不一致（键名里有 `/` 时文件名会被改写，而 `clip.name` 保持原名）。
- 清理子资产只删 `AssetDatabase.GetAssetPath(asset) == 我们这份文件` 的，**外部资产一根都不碰**。

## 6. EditorWindow 的状态不会自己活下来

窗口里的对象引用与列表在域重载后全丢。要么存 `EditorPrefs`，
要么存"可还原的坐标"（根物体资产路径 + 每个网格相对根物体的 `CalculateTransformPath`），
打开时按路径找回。纯对象引用没法存，别试。

## 7. 同一个 `TextAsset` 被改写，`GetInstanceID()` 不变

症状：改完配置文件（保存、或外部编辑器改了这个 `.json`），运行中的会话**还用着旧的解析结果**
—— 表现是"改了没反应"，而重进播放就好了。

原因：`AssetDatabase.ImportAsset` 之后 TextAsset 的实例常常还是同一个，凡是"按实例 ID 做的缓存"
（会话的配置指纹、组件里 `profile` 的解析缓存）都不会失效。

怎么办：指纹里除了实例 ID，再带上**文本长度 + 解析出来的那个数据对象的身份**
（`rig.Middleware` 在长度变化或显式 `ReloadProfile()` 之后会重新解析）；写完文件的窗口保存后
主动调一次 `ReloadProfile()`。（这一条是实跑用例抓出来的：换配置之后权重不动，`param` 卡在旧值上。）

## 7. `.meta` 可以手写

Unity 自己会生成，但**提交时不能等它**（有过"补上 .meta"的补丁提交）。
脚本用：

```yaml
fileFormatVersion: 2
guid: <32 位小写十六进制，自己生成一个>
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

`.md` / 文件夹用 `TextScriptImporter`（或只留 `fileFormatVersion` + `guid` 两行，Unity 会补齐）。
**新建文件夹也要给 `.meta`**（`docs/archive.meta` 就是先例）。
