using System;
using System.Collections.Generic;
using System.IO;
using Hollow.HoUnityTools.MmdMotion;
using Hollow.HoUnityTools.PlaySpeed;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Runs after HoVmdSequencePlayer.LateUpdate and validates real Unity frame timing.
[DefaultExecutionOrder(31000)]
public sealed class PlaySpeedProbe : MonoBehaviour
{
    private static readonly float[] Speeds = { 1, .25f, .5f, 0, 1, 2, 4, 1 };
    private readonly List<string> passed = new List<string>();
    private HoVmdSequencePlayer sequence;
    private AudioSource music;
    private AudioClip clip;
    private int stage, frames;
    private float previousTime;
    private const float FrameTime = 1f / 60;

    private void Start()
    {
        try
        {
            Time.captureDeltaTime = FrameTime;
            Time.timeScale = 1;
            var root = new GameObject("Sequence with soundtrack");
            root.SetActive(false);
            music = root.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.volume = 0;
            music.pitch = .8f;
            clip = AudioClip.Create("30 second test soundtrack", 30 * 48000, 1, 48000, false);
            music.clip = clip;
            root.AddComponent<AudioListener>();
            sequence = root.AddComponent<HoVmdSequencePlayer>();
            sequence.Music = music;
            sequence.Preroll = 0;
            root.SetActive(true);
            sequence.Restart();
            previousTime = sequence.CurrentTime;
        }
        catch (Exception error) { Finish(error); }
    }

    private void LateUpdate()
    {
        if (sequence == null) return;
        try
        {
            if (stage < Speeds.Length)
            {
                float speed = Speeds[stage];
                if (frames > 0)
                {
                    Near(Time.deltaTime, FrameTime * speed, "Unity scaled delta " + speed);
                    Near(sequence.CurrentTime - previousTime, Time.deltaTime, "Sequence scaled clock " + speed);
                    if (speed > 0 && speed <= 3)
                    {
                        Near(music.pitch, speed, "Audio pitch " + speed);
                        Check(music.isPlaying, "Audio is playing at " + speed);
                    }
                    else Check(!music.isPlaying, "Audio paused at " + speed);
                }
                previousTime = sequence.CurrentTime;
                if (++frames < 8) return;
                passed.Add("BGM clock, pitch and pause at " + speed + "x");
                frames = 0;
                stage++;
                if (stage < Speeds.Length) Time.timeScale = Speeds[stage];
                else sequence.Playing = false;
                return;
            }

            switch (stage)
            {
                case 8:
                    Near(sequence.CurrentTime, previousTime, "Player pause freezes clock");
                    Check(!music.isPlaying, "Player pause stops BGM");
                    sequence.Playing = true;
                    Time.timeScale = 0;
                    stage++;
                    break;
                case 9:
                    Near(sequence.CurrentTime, previousTime, "Resume while timeScale is zero stays frozen");
                    Check(!music.isPlaying, "Resume while timeScale is zero keeps BGM paused");
                    Time.timeScale = 1;
                    stage++;
                    break;
                case 10:
                    Near(sequence.CurrentTime - previousTime, Time.deltaTime, "Resume advances once");
                    Check(music.isPlaying, "BGM resumes");
                    passed.Add("Player pause / resume, including resume at 0x");
                    sequence.ExternallyDriven = true;
                    sequence.ExternalTime = 7.25f;
                    Time.timeScale = 0;
                    stage++;
                    break;
                case 11:
                    Near(sequence.CurrentTime, 7.25f, "Offline external clock ignores timeScale");
                    Near(music.pitch, .8f, "External control restores original audio pitch");
                    Check(!music.isPlaying, "External control stops interactive BGM");
                    passed.Add("External capture clock and audio ownership");
                    sequence.ExternallyDriven = false;
                    sequence.Music = null;
                    previousTime = sequence.CurrentTime;
                    Time.timeScale = .5f;
                    stage++;
                    break;
                case 12:
                    Near(sequence.CurrentTime - previousTime, Time.deltaTime, "Clock without BGM");
                    passed.Add("Scaled clock without BGM");
                    sequence.Duration = .01f;
                    sequence.Restart();
                    Time.timeScale = 1;
                    stage++;
                    break;
                case 13:
                    Near(sequence.CurrentTime, .01f, "Last sample is clamped to duration");
                    Check(!sequence.Playing, "Non-looping sequence stops at duration");
                    Check(HoPlaySpeedControl.Owner != sequence, "End of sequence releases playback ownership");
                    passed.Add("End of sequence samples exact final time");
                    sequence.Duration = .02f;
                    typeof(HoVmdSequencePlayer).GetField("loop",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(sequence, true);
                    sequence.Restart();
                    Time.timeScale = 4;
                    stage++;
                    break;
                case 14:
                    Near(sequence.CurrentTime, Time.deltaTime % .02f, "High-speed loop keeps remainder");
                    passed.Add("High-speed loop wrap");
                    sequence.enabled = false;
                    Finish(null);
                    break;
            }
        }
        catch (Exception error) { Finish(error); }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
    }

    private static void Near(float actual, float expected, string label)
    {
        Check(Mathf.Abs(actual - expected) < .0001f, label + ": expected " + expected + ", actual " + actual);
    }

    private void Finish(Exception error)
    {
        Time.timeScale = 1;
        Time.captureDeltaTime = 0;
        string report = string.Join("\n", passed.ToArray());
        report += error == null ? "\nPASS: all play speed regressions." : "\nFAIL: " + error;
        File.WriteAllText("playspeed-results.txt", report);
        Debug.Log(report);
#if UNITY_EDITOR
        if (error == null) EditorApplication.isPlaying = false;
        else EditorApplication.Exit(1);
#endif
    }
}
