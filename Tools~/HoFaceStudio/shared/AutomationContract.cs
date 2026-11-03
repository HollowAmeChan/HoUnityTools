using System;
using System.Linq;

namespace HoFace.Automation
{
    public static class AutomationContract
    {
        public const int Version = 2;
        public static string PipeName
        {
            get
            {
                string name = Environment.GetEnvironmentVariable("HOFACE_STUDIO_PIPE");
                if (string.IsNullOrWhiteSpace(name)) return "HoFaceStudio";
                if (name.Length > 80 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-' && c != '_'))
                    throw new ArgumentException("HOFACE_STUDIO_PIPE 仅接受不超过 80 字符的字母、数字、点、横线和下划线。");
                return name;
            }
        }
        public static readonly CommandDefinition[] Commands =
        {
            new CommandDefinition("capabilities", "列出协议、命令与参数", "", true),
            new CommandDefinition("status", "读取作品、输入、输出、选区与历史状态", "", true),
            new CommandDefinition("document", "读取完整已应用作品及待应用文本状态", "", true, true),
            new CommandDefinition("list", "列出参数行、IR 节点、目标与参数定义", "filter?:string", true, true),
            new CommandDefinition("selection", "读取当前行/节点、曲线点、游标与视口", "", true),
            new CommandDefinition("diagnostics", "读取当前 values、weights 与校验结果", "", true, true),
            new CommandDefinition("screenshot", "将 WPF 内容渲染为 PNG，无需操作桌面", "path?:string", true),
            new CommandDefinition("open", "校验并打开作品；默认保护未保存编辑", "valuePath:string, irPath:string, discardChanges?:bool"),
            new CommandDefinition("save", "应用有效文本并保存；可指定另存路径", "valuePath?:string, irPath?:string", false, true),
            new CommandDefinition("undo", "撤销一次作品编辑，保持当前选择", "", false, true),
            new CommandDefinition("redo", "重做一次作品编辑，保持当前选择", "", false, true),
            new CommandDefinition("selectValue", "按名称选择输出行；重名时必须指定行号", "parameter:string, outputIndex?:int", false, true),
            new CommandDefinition("selectNode", "按稳定 ID 选择 IR 节点", "id:string", false, true),
            new CommandDefinition("selectPoint", "选择当前图形中的曲线点或混合子项", "index:int", false, true),
            new CommandDefinition("setValueCurveKey", "原子修改曲线点；切线可使用字符串 Infinity", "parameter:string, outputIndex?:int, index:int, time?:number, value?:number, inTangent?:number|string, outTangent?:number|string", false, true),
            new CommandDefinition("addValueCurveKey", "添加曲线点", "parameter:string, outputIndex?:int, time:number, value:number, inTangent?:number|string, outTangent?:number|string", false, true),
            new CommandDefinition("deleteValueCurveKey", "删除曲线点，至少保留两个", "parameter:string, outputIndex?:int, index:int", false, true),
            new CommandDefinition("setValueExpression", "修改表达式", "parameter:string, outputIndex?:int, expression:string", false, true),
            new CommandDefinition("setNode", "修改 IR 显示名和参数轴", "id:string, name?:string, parameterX?:string, parameterY?:string", false, true),
            new CommandDefinition("setIrChild", "修改 1D/2D 子项或 Direct 权重", "nodeId:string, childId?:string, index?:int, property:x|y|threshold|weight, value:number", false, true),
            new CommandDefinition("setPoseWeight", "修改姿势目标权重", "nodeId:string, targetId:string, value:number", false, true),
            new CommandDefinition("setDirectWeight", "修改 Direct 常量权重", "nodeId:string, childId:string, value:number", false, true),
            new CommandDefinition("text", "校验并替换完整作品文本", "value:string, ir:string", false, true),
            new CommandDefinition("draftText", "设置尚未应用的 JSON 编辑草稿", "value?:string, ir?:string", false, true),
            new CommandDefinition("applyText", "校验并应用 JSON 草稿", "", false, true),
            new CommandDefinition("revertText", "还原尚未应用的 JSON 草稿", "", false, true),
            new CommandDefinition("applyProperties", "校验并应用属性面板草稿", "", false, true),
            new CommandDefinition("revertProperties", "还原属性面板草稿", "", false, true),
            new CommandDefinition("mode", "切换互斥输出模式", "value:Preview|RawForward, rawForwardPort?:int"),
            new CommandDefinition("view", "读取或设置分栏、标签页、折叠区与窗口尺寸", "structureWidth?:number, plotRatio?:number, structureTab?:0|1, centerTab?:0|1, showConnection?:bool, showFiles?:bool, showEditor?:bool, width?:number, height?:number, reset?:bool"),
            new CommandDefinition("cursor", "设置临时输入并实际求值；不写作品", "x:number, y?:number", false, true),
            new CommandDefinition("clearPreview", "清除手动预览结果与临时输入", "", false, true),
            new CommandDefinition("evaluate", "以共享核心独立求值一帧 raw；不改变实时会话", "raw:object<string,number>, time?:number", true, true),
            new CommandDefinition("vtsStart", "连接 VTS 输入", "phoneIp?:string, phonePort?:int, localPort?:int", false, true),
            new CommandDefinition("vtsStop", "停止 VTS 并释放输出", ""),
            new CommandDefinition("previewStart", "启用 Blender 权重发送，仅限 Preview 模式", "", false, true),
            new CommandDefinition("previewStop", "释放 Blender 输出并停止发送", ""),
            new CommandDefinition("recordStart", "开始 JSONL 原始输入录制", "path:string", false, true),
            new CommandDefinition("recordStop", "停止录制", ""),
            new CommandDefinition("replayOpen", "打开 JSONL 录制", "path:string", false, true),
            new CommandDefinition("replayPlay", "播放录制", "", false, true),
            new CommandDefinition("replayPause", "暂停录制", ""),
            new CommandDefinition("replaySeek", "跳转录制时间", "time:number", false, true),
            new CommandDefinition("shutdown", "关闭应用；默认保护未保存编辑", "discardChanges?:bool")
        };
        public static object Describe() => new
        {
            app = "HoFaceStudio", protocolVersion = Version, transport = "named-pipe", pipe = PipeName,
            commands = Commands.Select(x => new { name = x.Name, description = x.Description,
                payload = x.Payload, readOnly = x.ReadOnly, requiresDocument = x.RequiresDocument }).ToArray()
        };
    }
    public sealed class CommandDefinition
    {
        public string Name { get; }
        public string Description { get; }
        public string Payload { get; }
        public bool ReadOnly { get; }
        public bool RequiresDocument { get; }
        public CommandDefinition(string name, string description, string payload, bool readOnly = false, bool requiresDocument = false)
        { Name = name; Description = description; Payload = payload; ReadOnly = readOnly; RequiresDocument = requiresDocument; }
    }
}
