using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Hollow.HoUnityTools.MmdMotion
{
    [Serializable]
    public struct HoVmdBezier
    {
        public float x1, y1, x2, y2;
        public HoVmdBezier(float x1, float y1, float x2, float y2) { this.x1=x1;this.y1=y1;this.x2=x2;this.y2=y2; }
    }

    [Serializable]
    public struct HoVmdBoneInterpolation
    {
        public HoVmdBezier positionX, positionY, positionZ, rotation;
    }

    [Serializable]
    public struct HoVmdBoneFrame
    {
        public string boneName;
        public uint frame;
        public Vector3 position;
        public Quaternion rotation;
        public HoVmdBoneInterpolation interpolation;
    }

    [Serializable]
    public struct HoVmdMorphFrame
    {
        public string morphName;
        public uint frame;
        public float weight;
    }

    [Serializable]
    public struct HoVmdCameraInterpolation
    {
        public HoVmdBezier movement, rotation, distance, viewAngle;
    }

    [Serializable]
    public struct HoVmdCameraFrame
    {
        public uint frame;
        public float distance;
        public Vector3 targetPosition;
        public Vector3 rotation;
        public HoVmdCameraInterpolation interpolation;
        public uint viewAngle;
        public bool perspectiveOff;
    }

    [Serializable]
    public struct HoVmdLightFrame
    {
        public uint frame;
        public Color color;
        public Vector3 position;
    }

    [Serializable]
    public struct HoVmdSelfShadowFrame
    {
        public uint frame;
        public byte mode;
        public float distance;
    }

    [Serializable]
    public struct HoVmdIkToggle
    {
        public string boneName;
        public bool enabled;
    }

    [Serializable]
    public struct HoVmdShowIkFrame
    {
        public uint frame;
        public bool show;
        public HoVmdIkToggle[] toggles;
    }

    /// <summary>Imported VMD tracks. It is deliberately data-only; a player chooses the target skeleton and IK policy.</summary>
    public sealed class HoVmdAnimation : ScriptableObject
    {
        [HideInInspector] public string modelName;
        [HideInInspector] public string signature;
        [HideInInspector] public List<HoVmdBoneFrame> boneFrames = new List<HoVmdBoneFrame>();
        [HideInInspector] public List<HoVmdMorphFrame> morphFrames = new List<HoVmdMorphFrame>();
        [HideInInspector] public List<HoVmdCameraFrame> cameraFrames = new List<HoVmdCameraFrame>();
        [HideInInspector] public List<HoVmdLightFrame> lightFrames = new List<HoVmdLightFrame>();
        [HideInInspector] public List<HoVmdSelfShadowFrame> selfShadowFrames = new List<HoVmdSelfShadowFrame>();
        [HideInInspector] public List<HoVmdShowIkFrame> showIkFrames = new List<HoVmdShowIkFrame>();

        public uint LastBoneFrame => Last(boneFrames, x => x.frame);
        public uint LastMorphFrame => Last(morphFrames, x => x.frame);
        public uint LastCameraFrame => Last(cameraFrames, x => x.frame);
        public uint LastShowIkFrame => Last(showIkFrames, x => x.frame);

        private static uint Last<T>(List<T> values, Func<T,uint> frame)
        {
            uint result=0;
            if(values==null)return result;
            for(int i=0;i<values.Count;i++)if(frame(values[i])>result)result=frame(values[i]);
            return result;
        }

        public static HoVmdAnimation Parse(byte[] bytes)
        {
            if(bytes==null||bytes.Length==0)throw new InvalidDataException("VMD data is empty.");
            using(var stream=new MemoryStream(bytes,false))
            using(var reader=new BinaryReader(stream))
            {
                var asset=CreateInstance<HoVmdAnimation>();
                string signature=ReadFixed(reader,30,Encoding.ASCII); asset.signature=signature;
                int modelBytes=signature.StartsWith("Vocaloid Motion Data 0002",StringComparison.Ordinal)?20:10;
                if(!signature.StartsWith("Vocaloid Motion Data file",StringComparison.Ordinal)&&!signature.StartsWith("Vocaloid Motion Data 0002",StringComparison.Ordinal))
                    throw new InvalidDataException("Invalid VMD signature: "+signature);
                asset.modelName=ReadFixed(reader,modelBytes,GetCp932());
                uint count=reader.ReadUInt32();
                for(uint i=0;i<count;i++)asset.boneFrames.Add(ReadBone(reader));
                count=reader.ReadUInt32();
                for(uint i=0;i<count;i++)asset.morphFrames.Add(ReadMorph(reader));
                if(stream.Position+4<=stream.Length){count=reader.ReadUInt32();for(uint i=0;i<count;i++)asset.cameraFrames.Add(ReadCamera(reader));}
                if(stream.Position+4<=stream.Length){count=reader.ReadUInt32();for(uint i=0;i<count;i++)asset.lightFrames.Add(new HoVmdLightFrame{frame=reader.ReadUInt32(),color=new Color(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),1),position=ReadVector(reader)});}
                if(stream.Position+4<=stream.Length){count=reader.ReadUInt32();for(uint i=0;i<count;i++)asset.selfShadowFrames.Add(new HoVmdSelfShadowFrame{frame=reader.ReadUInt32(),mode=reader.ReadByte(),distance=reader.ReadSingle()});}
                if(stream.Position+4<=stream.Length){count=reader.ReadUInt32();for(uint i=0;i<count;i++)asset.showIkFrames.Add(ReadShowIk(reader));}
                return asset;
            }
        }

        private static HoVmdBoneFrame ReadBone(BinaryReader r)
        {
            string name=ReadFixed(r,15,GetCp932()); uint frame=r.ReadUInt32(); Vector3 position=ReadVector(r); Quaternion rotation=ReadQuaternion(r); byte[] interpolation=r.ReadBytes(64);
            return new HoVmdBoneFrame{boneName=name,frame=frame,position=position,rotation=rotation,interpolation=new HoVmdBoneInterpolation{positionX=BoneBezier(interpolation,0),positionY=BoneBezier(interpolation,1),positionZ=BoneBezier(interpolation,2),rotation=BoneBezier(interpolation,3)}};
        }

        private static HoVmdMorphFrame ReadMorph(BinaryReader r)=>new HoVmdMorphFrame{morphName=ReadFixed(r,15,GetCp932()),frame=r.ReadUInt32(),weight=r.ReadSingle()};

        private static HoVmdCameraFrame ReadCamera(BinaryReader r)
        {
            uint frame=r.ReadUInt32(); float distance=r.ReadSingle(); Vector3 target=ReadVector(r); Vector3 rotation=ReadVector(r); byte[] raw=r.ReadBytes(24);
            return new HoVmdCameraFrame{frame=frame,distance=distance,targetPosition=target,rotation=rotation,interpolation=new HoVmdCameraInterpolation{movement=CameraBezier(raw,0),rotation=CameraBezier(raw,12),distance=CameraBezier(raw,16),viewAngle=CameraBezier(raw,20)},viewAngle=r.ReadUInt32(),perspectiveOff=r.ReadByte()!=0};
        }

        private static HoVmdShowIkFrame ReadShowIk(BinaryReader r)
        {
            uint frame=r.ReadUInt32(); bool show=r.ReadByte()!=0; uint count=r.ReadUInt32(); var toggles=new HoVmdIkToggle[count];
            for(uint i=0;i<count;i++)toggles[i]=new HoVmdIkToggle{boneName=ReadFixed(r,20,GetCp932()),enabled=r.ReadByte()!=0};
            return new HoVmdShowIkFrame{frame=frame,show=show,toggles=toggles};
        }

        private static HoVmdBezier BoneBezier(byte[] raw,int channel)
        { int offset=channel<2?channel:channel+15; return Bezier(raw,offset,offset+4,offset+8,offset+12); }
        private static HoVmdBezier CameraBezier(byte[] raw,int offset)
        { if(offset==0){return Bezier(raw,0,2,1,3);} return Bezier(raw,offset,offset+2,offset+1,offset+3); }
        private static HoVmdBezier Bezier(byte[] raw,int x1,int y1,int x2,int y2)=>new HoVmdBezier(raw[x1]/127.0f,raw[y1]/127.0f,raw[x2]/127.0f,raw[y2]/127.0f);
        private static Vector3 ReadVector(BinaryReader r)=>new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        private static Quaternion ReadQuaternion(BinaryReader r)=>new Quaternion(-r.ReadSingle(),r.ReadSingle(),-r.ReadSingle(),r.ReadSingle()).normalized;
        private static Encoding GetCp932(){try{return Encoding.GetEncoding(932);}catch{return Encoding.UTF8;}}
        private static string ReadFixed(BinaryReader r,int bytes,Encoding encoding){byte[] data=r.ReadBytes(bytes);int length=Array.IndexOf(data,(byte)0);if(length<0)length=data.Length;return encoding.GetString(data,0,length).Trim();}
    }
}
