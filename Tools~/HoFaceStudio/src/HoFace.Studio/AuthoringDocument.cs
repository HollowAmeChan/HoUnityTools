using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;

namespace HoFace.Studio
{
    public sealed class AuthoringSnapshot
    {
        public string Value, Ir;
    }
    public sealed class AuthoringDocument
    {
        public HoFaceMiddleware Value { get; private set; }
        public HoFaceIrDocument Ir { get; private set; }
        public string ValuePath { get; private set; }
        public string IrPath { get; private set; }
        private string diskValue, diskIr;
        private string savedValue, savedIr;
        private readonly Stack<AuthoringSnapshot> undo = new Stack<AuthoringSnapshot>();
        private readonly Stack<AuthoringSnapshot> redo = new Stack<AuthoringSnapshot>();
        public bool CanUndo => undo.Count != 0;
        public bool CanRedo => redo.Count != 0;
        public void Edit(Action<HoFaceMiddleware, HoFaceIrDocument> edit)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            var before = Snapshot();
            try
            {
                edit(Value, Ir);
                Remember(before);
            }
            catch
            {
                Restore(before);
                throw;
            }
        }
        public bool IsDirty => Value != null && (Snapshot().Value != savedValue || Snapshot().Ir != savedIr);
        public AuthoringSnapshot Snapshot() => new AuthoringSnapshot { Value = HoFaceValueDocument.Write(Value), Ir = Ir.Write() };
        public void Open(string valuePath, string irPath)
        {
            string value = File.ReadAllText(valuePath), ir = File.ReadAllText(irPath);
            Parse(value, ir, out var profile, out var graph);
            Value = profile; Ir = graph; ValuePath = valuePath; IrPath = irPath;
            diskValue = value; diskIr = ir;
            savedValue = Snapshot().Value; savedIr = Snapshot().Ir;
            undo.Clear(); redo.Clear();
        }
        public static void Parse(string value, string ir, out HoFaceMiddleware profile, out HoFaceIrDocument graph)
        {
            if (!HoFaceValueDocument.TryParse(value, out profile, out string error)) throw new FormatException(error);
            var evaluator = new HoVtsRuleEvaluator(profile);
            if (!string.IsNullOrEmpty(evaluator.Error)) throw new FormatException(evaluator.Error);
            if (!HoFaceIrDocument.TryParse(ir, out graph, out error)) throw new FormatException(error);
        }
        public void Remember(AuthoringSnapshot before)
        {
            var after = Snapshot();
            if (before.Value == after.Value && before.Ir == after.Ir) return;
            try { Parse(after.Value, after.Ir, out _, out _); }
            catch { Restore(before); throw; }
            undo.Push(before); redo.Clear();
        }
        public void ApplyText(string value, string ir)
        {
            Parse(value, ir, out var profile, out var graph);
            var before = Snapshot();
            Value = profile; Ir = graph;
            Remember(before);
        }
        private void Restore(AuthoringSnapshot snapshot)
        {
            Parse(snapshot.Value, snapshot.Ir, out var profile, out var graph);
            Value = profile; Ir = graph;
        }
        public void Undo()
        {
            if (undo.Count == 0) return;
            var previous = undo.Pop(); redo.Push(Snapshot()); Restore(previous);
        }
        public void Redo()
        {
            if (redo.Count == 0) return;
            var next = redo.Pop(); undo.Push(Snapshot()); Restore(next);
        }
        public void Save(string valuePath = null, string irPath = null)
        {
            var snapshot = Snapshot();
            Parse(snapshot.Value, snapshot.Ir, out _, out _);
            valuePath = valuePath ?? ValuePath; irPath = irPath ?? IrPath;
            if (valuePath == ValuePath && File.ReadAllText(valuePath) != diskValue)
                throw new IOException("value 文件已被外部修改。请重新载入或另存为，避免覆盖。");
            if (irPath == IrPath && File.ReadAllText(irPath) != diskIr)
                throw new IOException("IR 文件已被外部修改。请重新载入或另存为，避免覆盖。");
            if (Path.GetFullPath(valuePath) == Path.GetFullPath(irPath)) throw new IOException("两种文件不能使用同一路径。");
            AtomicWrite(valuePath, snapshot.Value);
            AtomicWrite(irPath, snapshot.Ir);
            ValuePath = valuePath; IrPath = irPath;
            diskValue = savedValue = snapshot.Value; diskIr = savedIr = snapshot.Ir;
        }
        private static void AtomicWrite(string path, string text)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, text, new UTF8Encoding(false)); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
