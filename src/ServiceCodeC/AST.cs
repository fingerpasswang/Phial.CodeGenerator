using System;
using System.Collections.Generic;
using System.IO;

namespace ServiceCodeC
{
    enum ServiceType
    {
        None,
        Notify,
        Service,
        Sync,
    }

    enum ServiceScope
    {
        InterServer,
        ClientToServer,
        ServerToClient,
    }

    class ServiceMeta
    {
        public uint Id;
        public string Name;
        public ServiceType Type;
        public ServiceScope Scope;
        public bool Divisional = true;
        public bool Multicast = false;
        public List<MethodMeta> Methods = new List<MethodMeta>();

        public MemoryStream GetVersionBytes()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            bw.Write(Name);
            bw.Write((int)Type);
            bw.Write((int)Scope);
            bw.Write(Divisional);

            foreach (var meta in Methods)
            {
                var stream = meta.GetVersionBytes();

                bw.Write(stream.GetBuffer(), 0, (int)stream.Length);
            }

            return ms;
        }
    }

    class MethodMeta
    {
        public uint Id;
        public string Name;
        public Type ReturnType;
        public List<ParameterMeta> Parameters = new List<ParameterMeta>();
        public ServiceMeta ServiceMeta;

        public MemoryStream GetVersionBytes()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            bw.Write(Id);
            bw.Write(Name);
            bw.Write(ReturnType.Name);

            foreach (var meta in Parameters)
            {
                var stream = meta.GetVersionBytes();

                bw.Write(stream.GetBuffer(), 0, (int)stream.Length);
            }

            return ms;
        }
    }

    class ParameterMeta
    {
        public Type Type;
        public string Name;
        public uint Position;

        public MemoryStream GetVersionBytes()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            bw.Write(Name);
            bw.Write(Type.Name);
            bw.Write(Position);

            return ms;
        }
    }
}
