# Unity YAML 与转储：哪些结论不能从转储里下

## 字段名跟直觉不一样

- 混合树的子节点字段是 **`m_Childs`**，不是 `m_Children`。
- 类型字段是 **`m_BlendType`**，不是 `m_Type`。
- 文档 id 写在 `--- !u!206 &-123` 里，**`&` 后面直接换行** —— 用 `split(' ')` 截会拿到空串。
  （`!u!206` = BlendTree，`!u!74` = AnimationClip，`!u!91` = AnimatorController，`!u!1101/1102` = 状态机迁移。）

## 片段是控制器的子资产，不是独立文件

`.controller` 里的片段与子树都是**同一个 YAML 文件里的子文档**。所以"这个片段是谁"要两步：
先用控制器同级 `.meta` 的 `guid` 确认是哪个资产，再在文件内部按 `&<fileID>` 找子文档。
只按名字找会在"同名不同层"上翻车。

## 两个会骗人的转储（别用它们下形状结论）

| 转储 | 骗在哪 | 后果 |
| --- | --- | --- |
| `inspect_arkit_controller.py` 的 `thr=` 列 | **等于 0 与 −1 的阈值不打印** | 一棵 3 子节点的 1D 树看起来像"两手两脚"，姿势数会数错 |
| `inspect_bigtree.py` 的类型标签 | `[1D]` / `[Simple1D]` / `[FreeDir2D]` **与原始 `m_BlendType` 不符** | 会把 `Simple1D` 当成 2D、据此说"它做了二维" |

**规则**：任何关于**树的形状**的结论，都要回到原始 YAML 复核一遍（`.research/` 里的脚本只是
有损视图，用来找位置，不用来定结论）。文档里每条一手结论都记了原始行号，就是为了这个。

## "没有找到"不等于"没有"

从别人包里只取回了一部分字节时（曾经只拿到某个模板的 ~23%），grep 零命中**不能**推出
"这条链不存在"。要么说清楚"只覆盖了 X%"，要么不下结论 —— 我们在这上面写错过一条。

## 现成的工具

```powershell
python .research/inspect_arkit_controller.py     # → .research/arkit-controller-report.txt
python .research/inspect_shared_controller.py    # → .research/shared-controller-report.txt
python .research/_lid_probe/summary.py           # 眼睑那几棵树的一手复现（脚本 + 输出都在那个目录）
```

读转储时按 **UTF-8** 读，否则中文会看起来像乱码（见 [文档与编码](DOCS_ENCODING.md)）。
