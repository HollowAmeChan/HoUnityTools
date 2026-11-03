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
    /// Unity-only authoring adapter for HoFaceStudio raw mode.
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
        private double lastPacketTime = -1;
        private string status = "未启动";

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
            if (socket == null && autoStart) StartListening();
            Poll();
        }

        public void StartListening()
        {
            StopListening();
            try
            {
                if (localPort < 1 || localPort > 65535) localPort = 21415;
                socket = new UdpClient(localPort);
                socket.Client.Blocking = false;
                status = "监听 127.0.0.1:" + ((IPEndPoint)socket.Client.LocalEndPoint).Port;
            }
            catch (Exception error)
            {
                socket = null;
                status = "启动失败：" + error.Message;
            }
        }

        public void StopListening()
        {
            socket?.Close();
            socket?.Dispose();
            socket = null;
            lastPacketTime = -1;
            if (status != "启动失败") status = "未启动";
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
                if (frame.Time < lastPacketTime)
                {
                    status = "丢弃倒退时间帧";
                    continue;
                }
                lastPacketTime = frame.Time;
                receiver.ApplyRawFrame(frame.Values, frame.Tracked,
                    Time.realtimeSinceStartupAsDouble, "HoFaceStudio/UnityRaw");
                status = frame.Tracked ? "Unity 原始输入运行中" : "Unity 已收到帧但未追踪";
            }
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
