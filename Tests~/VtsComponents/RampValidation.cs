// Compile with the production HoFaceVtsRamp.cs; pure numerical regression checks.
using System;
using HoFaceTracking.Core;

public static class HoVtsRampValidation
{
    public static void Main()
    {
        Near(.4f, HoFaceVtsRamp.Map(.4f, 0, 1, 0, 1, 1), "identity shape range");
        Near(.8f, HoFaceVtsRamp.Map(.4f, 0, 1, 0, 1, 2), "gain before final bounds");
        Near(1, HoFaceVtsRamp.Map(.9f, 0, 1, 0, 1, 2), "shape output cannot overflow");
        Near(0, HoFaceVtsRamp.Map(0, -180, 180, -180, 180, 2), "signed neutral remains zero when gain changes");
        Near(-30, HoFaceVtsRamp.Map(-15, -180, 180, -180, 180, 2), "negative pose gain");
        Near(30, HoFaceVtsRamp.Map(15, -180, 180, -180, 180, 2), "positive pose gain");
        Near(-180, HoFaceVtsRamp.Map(-100, -180, 180, -180, 180, 3), "negative final bound");
        Near(180, HoFaceVtsRamp.Map(100, -180, 180, -180, 180, 3), "positive final bound");
        Near(100, HoFaceVtsRamp.Map(.5f, 0, 1, 100, 0, 2), "reversed output range bounded after gain");
        Near(0, HoFaceVtsRamp.Map(90, -180, 180, -180, 180, 0), "zero gain preserves signed origin");
        Near(4, HoFaceVtsRamp.Map(4, 1, 1, 0, 1, 1), "invalid input range passes through");
        Near(4, HoFaceVtsRamp.Map(4, 0, 1, 0, 1, -1), "invalid gain passes through");
        Near(1, HoFaceVtsRamp.Map(float.MaxValue, -float.MaxValue, float.MaxValue, -1, 1, float.MaxValue), "extreme finite values stay bounded");
        if (!float.IsNaN(HoFaceVtsRamp.Map(float.NaN, 0, 1, 0, 1, 1))) throw new Exception("Invalid wire value must remain diagnosable");
        Console.WriteLine("HO_VTS_RAMP PASS assertions=14");
    }
    private static void Near(float expected, float actual, string name)
    {
        if (float.IsNaN(actual) || Math.Abs(expected - actual) > .0001f) throw new Exception(name + ": " + actual + " vs " + expected);
    }
}
