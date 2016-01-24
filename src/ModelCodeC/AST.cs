using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ModelCodeC
{
    class ModelMeta
    {
        public string Name
        {
            get { return Type.Name; }
        }
        public Type Type;
        public string MysqlTableName;
        public string RedisCacheName;
        public bool Serialize;
        public List<FieldMeta> Fields = new List<FieldMeta>();
        public FieldMeta Key;

        public bool IsHierarchy = false;
        public bool IsTopmostBase = false;
        public byte HierarchyCode;
        public ModelMeta Parent;
        public List<ModelMeta> Children = new List<ModelMeta>();

        public MemoryStream GetVersionBytes()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            bw.Write(Name);
            bw.Write(Type.Name);

            if (MysqlTableName != null)
            {
                bw.Write(MysqlTableName);
            }
            if (RedisCacheName != null)
            {
                bw.Write(RedisCacheName);
            }
            bw.Write(Serialize);
            foreach (var fieldMeta in Fields)
            {
                var stream = fieldMeta.GetVersionBytes();

                bw.Write(stream.GetBuffer(), 0, (int)stream.Length);
            }
            if (Key != null)
            {
                var stream = Key.GetVersionBytes();

                bw.Write(stream.GetBuffer(), 0, (int)stream.Length);
            }

            return ms;
        }
    }

    class FieldMeta
    {
        public string Name
        {
            get { return Field.Name; }
        }

        public Type Type
        {
            get { return Field.FieldType; }
        }
        public ModelMeta ModelMeta;
        public FieldInfo Field;
        public uint Position;
        public uint SerializeIndex;
        public bool IsInherit = false;

        public MemoryStream GetVersionBytes()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            bw.Write(Name);
            bw.Write(Type.Name);
            bw.Write(Position);
            bw.Write(SerializeIndex);
            bw.Write(IsInherit);

            return ms;
        }
    }
}
