#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hollow.HoUnityTools.FaceTracking;
using Hollow.HoUnityTools.FaceTracking.Core;
using UnityEngine;

namespace Hollow.HoUnityTools.Editor.FaceTracking
{
    /// <summary>
    /// Editor-only authoring adapter for HoFaceStudio raw mode.
    /// It forwards raw frames into the existing character receiver so value,
    /// LookAt and other Unity-side consumers continue to run in Unity.
    /// It is not part of the Warudo Mod runtime path.
    /// </summary>
    [AddComponentMenu("HoUnityTools/面捕/HoFaceStudio 原始输入")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-32000)]
    public sealed class HoFaceStudioRawInput : MonoBehaviour
    {
        [SerializeField] private HoVtsParameterReceiver parameterReceiver;
        [SerializeField] private int localPort = 21415;
        [SerializeField] private bool autoStart = true;
        private UdpClient socket;
        private float retryAt;
        private string status = "未启动";
        private const string RawSource = "HoFaceStudio/UnityRaw";

        public HoVtsParameterReceiver ParameterReceiver
        {
            get => parameterReceiver;
            set => parameterReceiver = value;
        }
        public int LocalPort { get => localPort; set => localPort = value; }
        public bool AutoStart { get => autoStart; set => autoStart = value; }
        public string Status => status;

        private void OnEnable()
        {
            if (autoStart) StartListening();
        }

        private void Update()
        {
            if (socket == null && autoStart && Time.realtimeSinceStartup >= retryAt) StartListening();
            Poll();
        }

        public void StartListening()
        {
            StopListening();
            try
            {
                if (localPort < 1 || localPort > 65535) localPort = 21415;
                socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, localPort));
                socket.Client.Blocking = false;
                retryAt = 0;
                status = "监听 loopback:" + ((IPEndPoint)socket.Client.LocalEndPoint).Port;
            }
            catch (Exception error)
            {
                socket = null;
                status = "启动失败：" + error.Message;
                retryAt = Time.realtimeSinceStartup + 2f;
            }
        }

        public void StopListening()
        {
            socket?.Close();
            socket?.Dispose();
            socket = null;
            if (parameterReceiver != null && parameterReceiver.Source == RawSource)
                parameterReceiver.HoVtsReleaseInput(RawSource);
            retryAt = 0;
            status = "未启动";
        }

        private void Poll()
        {
            if (socket == null) return;
            HoVtsParameterReceiver receiver = ResolveReceiver();
            if (receiver == null)
            {
                status = "找不到 HoVtsParameterReceiver";
                return;
            }
            int count = 0;
            while (socket.Available > 0 && count++ < 16)
            {
                IPEndPoint source = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes;
                try { bytes = socket.Receive(ref source); }
                catch (SocketException) { break; }
                if (!HoFaceRawFrameJson.TryParse(Encoding.UTF8.GetString(bytes),
                    out HoFaceRawFrame frame, out string error))
                {
                    status = "帧错误：" + error;
                    continue;
                }
                if (!IPAddress.IsLoopback(source.Address))
                {
                    status = "拒绝非本机 raw 帧";
                    continue;
                }
                try
                {
                    receiver.ApplyRawFrame(frame.Values, frame.Tracked,
                        Time.realtimeSinceStartupAsDouble, RawSource);
                    status = frame.Tracked ? "Unity 原始输入运行中" : "Unity 已收到帧但未追踪";
                    SendAck(source, frame.Sequence, true, status);
                }
                catch (Exception applyError)
                {
                    status = "拒绝 raw 帧：" + applyError.Message;
                    SendAck(source, frame.Sequence, false, status);
                }
            }
        }

        private void SendAck(IPEndPoint destination, long sequence, bool accepted, string message)
        {
            if (socket == null || destination == null) return;
            byte[] bytes = Encoding.UTF8.GetBytes(HoFaceRawFrameJson.WriteAck(sequence, accepted, message));
            try { socket.Send(bytes, bytes.Length, destination); }
            catch (SocketException) { status = "回执发送失败"; }
        }

        private HoVtsParameterReceiver ResolveReceiver()
        {
            if (parameterReceiver != null && parameterReceiver.isActiveAndEnabled) return parameterReceiver;
            var receivers = GetComponentsInChildren<HoVtsParameterReceiver>(true);
            return receivers.Length == 1 && receivers[0].isActiveAndEnabled ? receivers[0] : null;
        }

        private void OnDisable() => StopListening();
        private void OnDestroy() => StopListening();
    }
}
#endif
