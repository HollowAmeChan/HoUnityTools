using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HoFace.Studio
{
    public enum MonitorStage { Raw, Value, Ir }

    public sealed class AuthoringMonitorFrame
    {
        public IReadOnlyDictionary<string, float> Raw { get; }
        public IReadOnlyDictionary<string, float> RawEffective { get; }
        public IReadOnlyDictionary<string, float> Values { get; }
        public IReadOnlyDictionary<string, float> ValuesEffective { get; }
        public IReadOnlyDictionary<string, float> Weights { get; }
        public IReadOnlyDictionary<string, float> WeightsEffective { get; }
        public bool Tracked { get; }
        public double Time { get; }

        public AuthoringMonitorFrame(
            IReadOnlyDictionary<string, float> raw,
            IReadOnlyDictionary<string, float> rawEffective,
            IReadOnlyDictionary<string, float> values,
            IReadOnlyDictionary<string, float> valuesEffective,
            IReadOnlyDictionary<string, float> weights,
            IReadOnlyDictionary<string, float> weightsEffective,
            bool tracked,
            double time)
        {
            Raw = Snapshot(raw); RawEffective = Snapshot(rawEffective);
            Values = Snapshot(values); ValuesEffective = Snapshot(valuesEffective);
            Weights = Snapshot(weights); WeightsEffective = Snapshot(weightsEffective);
            Tracked = tracked; Time = time;
        }

        private static IReadOnlyDictionary<string, float> Snapshot(IReadOnlyDictionary<string, float> source) =>
            source == null ? new Dictionary<string, float>(StringComparer.Ordinal) :
                new Dictionary<string, float>(source, StringComparer.Ordinal);
    }

    public sealed class MonitorParameterRow : INotifyPropertyChanged
    {
        private float liveValue, value, minimum, maximum;
        private float? overrideValue;
        private bool isFresh;

        public MonitorStage Stage { get; }
        public string Key { get; }
        public string Label { get; }
        public float LiveValue { get => liveValue; private set => Set(ref liveValue, value); }
        public float Value { get => this.value; private set => Set(ref this.value, value); }
        public float Minimum { get => minimum; private set => Set(ref minimum, value); }
        public float Maximum { get => maximum; private set => Set(ref maximum, value); }
        public bool IsFresh { get => isFresh; private set => Set(ref isFresh, value); }
        public float? OverrideValue { get => overrideValue; private set => Set(ref overrideValue, value); }
        public bool IsOverridden => OverrideValue.HasValue;
        public string ValueText => Format(Value);
        public string LiveText => IsOverridden ? (IsFresh ? "实时 " + Format(LiveValue) : "等待输入") : "";
        public string FreshText => IsFresh ? "" : "等待帧";
        public string DetailText => Key + "\n当前值 " + Format(Value) +
            (IsOverridden ? "\n实时值 " + (IsFresh ? Format(LiveValue) : "等待输入") : "");
        public event PropertyChangedEventHandler PropertyChanged;

        public MonitorParameterRow(MonitorStage stage, string key, string label, float initialValue, float minimum = -1, float maximum = 1)
        {
            Stage = stage; Key = key; Label = string.IsNullOrWhiteSpace(label) ? key : label;
            liveValue = value = initialValue;
            this.minimum = minimum; this.maximum = maximum;
            if (maximum <= minimum) { this.minimum = -1; this.maximum = 1; }
        }

        public void Update(float? live, float? effective, float? pinned)
        {
            IsFresh = live.HasValue;
            if (live.HasValue) { LiveValue = live.Value; Include(live.Value); }
            OverrideValue = pinned;
            if (effective.HasValue) Value = effective.Value;
            else if (pinned.HasValue) Value = pinned.Value;
            else if (live.HasValue) Value = live.Value;
            Include(Value);
            OnPropertyChanged(nameof(IsOverridden)); OnPropertyChanged(nameof(ValueText)); OnPropertyChanged(nameof(LiveText));
            OnPropertyChanged(nameof(FreshText)); OnPropertyChanged(nameof(DetailText));
        }

        public void SetPinnedValue(float? pinned)
        {
            OverrideValue = pinned;
            if (pinned.HasValue) { Value = pinned.Value; Include(pinned.Value); }
            else Value = LiveValue;
            IsFresh = pinned.HasValue || IsFresh;
            OnPropertyChanged(nameof(IsOverridden)); OnPropertyChanged(nameof(ValueText)); OnPropertyChanged(nameof(LiveText));
            OnPropertyChanged(nameof(FreshText)); OnPropertyChanged(nameof(DetailText));
        }

        private void Include(float sample)
        {
            if (sample < Minimum) Minimum = sample - Math.Max(.05f, Math.Abs(sample) * .1f);
            if (sample > Maximum) Maximum = sample + Math.Max(.05f, Math.Abs(sample) * .1f);
            if (Maximum - Minimum < 1e-5f) { Minimum -= .5f; Maximum += .5f; }
        }

        private static string Format(float number) => number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        private void Set<T>(ref T field, T value, [CallerMemberName] string property = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnPropertyChanged(property);
        }

        private void OnPropertyChanged([CallerMemberName] string property = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
