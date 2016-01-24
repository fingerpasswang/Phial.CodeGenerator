using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CodeC;

namespace ModelCodeC
{
    class ModelGenerator
    {
        ICoder<IEnumerable<ModelMeta>> GenSerializeCoder(bool isClient)
        {
            // [FieldIndex(Index = 1)]
            // public UInt32 TowerId;
            var fieldDec =
                "[FieldIndex(Index = {0})]".Basic<FieldMeta>(meta => meta.SerializeIndex)
                .Satisfy(meta => meta.SerializeIndex > 0 && !isClient)
                .SkipLine()
                .Append("public {0} {1};".Basic<FieldMeta>(meta => CoderHelper.GetTypeStr(meta.Field.FieldType),
                        meta => meta.Field.Name))
                .Satisfy(meta => !meta.IsInherit);

            var fieldWriterWithCheck =
                CoderHelper.WriteWithCheckCoder<FieldMeta>()
                .Lift(
                    (FieldMeta meta) =>
                    new Tuple<Type, ICoder<FieldMeta>, FieldMeta>(meta.Type, "{0}".Basic<FieldMeta>(f => f.Name),
                            meta));

            var writer =
                Generator.GenSelect(
                    "public void Write(BinaryWriter bw)".Unit<ModelMeta>()
                    .Combine(fieldWriterWithCheck.Many("\n").Brace(), meta => meta.Fields)
                    .Satisfy(meta => !meta.IsHierarchy)
                    ,
                    "public {0} void Write(BinaryWriter bw)".Basic<ModelMeta>(meta =>
                            Generator.GenSelect(
                                "override".Unit<ModelMeta>().Satisfy(meta1 => !meta1.IsTopmostBase)
                                , "virtual".Unit<ModelMeta>()).Code(meta))
                    .Append(
                        "bw.Write((byte){0});".Basic<ModelMeta>(meta => meta.HierarchyCode)
                        .Combine(fieldWriterWithCheck.Many("\n"), meta => meta.Fields)
                        .Brace()));

            var fieldReaderWithCheck =
                CoderHelper.ReadWithCheckCoder<FieldMeta>()
                .Lift(
                    (FieldMeta meta) =>
                    new Tuple<Type, ICoder<FieldMeta>, FieldMeta>(meta.Type, "{0}".Basic<FieldMeta>(f => f.Name),
                            meta));

            var reader =
                Generator.GenSelect(
                    "public {0} Read(BinaryReader br)".Basic<ModelMeta>(meta => CoderHelper.GetTypeStr(meta.Type)).Satisfy(meta => !meta.IsHierarchy)
                    , "public new {0} ReadImpl(BinaryReader br)".Basic<ModelMeta>(meta => CoderHelper.GetTypeStr(meta.Type)))
                .Combine(fieldReaderWithCheck.Many("\n").Append("return this;".Unit<IEnumerable<FieldMeta>>()).Brace(), meta => meta.Fields);

            var hierarchyReader =
                "public new {0} Read(BinaryReader br){{return ({0})ReadStatic(br);}}".Basic<ModelMeta>(
                    meta => CoderHelper.GetTypeStr(meta.Type)).Satisfy(meta => meta.IsHierarchy);

            var topmostReaderDispatcher =
                "public static {0} ReadStatic(BinaryReader br)".Basic<ModelMeta>(
                    meta => CoderHelper.GetTypeStr(meta.Type))
                .Append(
                    "byte tp = br.ReadByte();if (tp != (byte)SerializeObjectMark.IsNull){{switch (tp){{{0}{1}}}}}return null;"
                    .Basic<ModelMeta>(meta => "case {0}: return (new {1}()).ReadImpl(br);".Basic<ModelMeta>(
                                          metac => metac.HierarchyCode, metac => CoderHelper.GetTypeStr(metac.Type))
                                      .Many("\n")
                                      .Code(meta.Children), meta => "default: return (new {0}()).ReadImpl(br);".Basic<ModelMeta>(metai => CoderHelper.GetTypeStr(metai.Type)).Code(meta)).Brace());

            var dumpAtomic =
                Generator.GenSelect(
                    "{0}!=null?string.Join(\", \", Array.ConvertAll({0}, input => input.ToString())):\"null\"".Basic<FieldMeta>(
                        meta => meta.Field.Name).Satisfy(meta => meta.Field.FieldType.IsArray)
                    ,
                    "{0}!=null?string.Join(\", \", Array.ConvertAll({0}.ToArray(), input => input.ToString())):\"null\"".Basic<FieldMeta>(
                        meta => meta.Field.Name).Satisfy(meta => meta.Field.FieldType.Name.Equals("List`1"))
                    ,
                    "{0}".Basic<FieldMeta>(meta => meta.Field.Name));

            var dumpHelpAtomic = "{0}={{{1}}}\\n".Basic<FieldMeta>(meta => meta.Field.Name, meta => meta.Position - 1);

            var dump =
                "public override string ToString(){{return string.Format(\"{0}:\\n{1}\", {2});}}"
                .Basic<ModelMeta>(meta => meta.Type.Name, meta => dumpHelpAtomic.Many("").Code(meta.Fields),
                                  meta => dumpAtomic.Many(",").Code(meta.Fields));

            var serialize =
                "public class {0}{1}{{".Basic<ModelMeta>(meta => meta.Type.Name, meta => ":{0}".Basic<ModelMeta>(meta1 => CoderHelper.GetTypeStr(meta1.Parent.Type)).Satisfy(meta1 => meta1.Parent != null).Code(meta))
                .Combine(fieldDec.Many("\n"), meta => meta.Fields)
                .Append(writer.SkipLine())
                .Append(reader.SkipLine())
                .Append(hierarchyReader.SkipLine().Satisfy(meta => meta.IsHierarchy))
                .Append(topmostReaderDispatcher.SkipLine().Satisfy(meta => meta.IsTopmostBase))
                .Append(dump.SkipLine())
                .WithPostfix("}");

            return serialize.Many("\n");
            //return "".Unit<TypeMeta>().Combine(dumpAtomic.Many(","), meta => meta.Fields).Many("\n");
        }

        ICoder<IEnumerable<ModelMeta>> GenDataAccessCoder()
        {
            Func<FieldMeta, bool> isKey = meta => meta.ModelMeta.Key == meta;
            var loadFieldMethodName =
                "Load{0}At{1}Async".Basic<FieldMeta>(meta => meta.Name, meta => meta.ModelMeta.Name);
            var updateFieldMethodName =
                "Update{0}At{1}".Basic<FieldMeta>(meta => meta.Name, meta => meta.ModelMeta.Name);

            var fieldPara =
                "{0} {1}".Basic<FieldMeta>(meta => CoderHelper.GetTypeStr(meta.Type), meta => meta.Name.ToLower());
            var modelFieldPara =
                "{0} {1}".Basic<ModelMeta>(meta => CoderHelper.GetTypeStr(meta.Type), meta => meta.Name.ToLower());

            // Task<uint> LoadUidAtPlayerInfoAsync()
            var loadField =
                "{0} {1}()".Basic<FieldMeta>(meta => CoderHelper.TaskTypeCoder().Code(meta.Type),
                                             loadFieldMethodName.Code);

            // void UpdateUidAtPlayerInfo(uint uid)
            var updateField =
                "void {0}({1})".Basic<FieldMeta>(updateFieldMethodName.Code, meta => fieldPara.Code(meta));

            // Task<PlayerInfo> LoadPlayerInfoAsync(ulong pid)
            var loadModel =
                "{0} Load{1}Async()".Basic<FieldMeta>(meta => CoderHelper.TaskTypeCoder().Code(meta.ModelMeta.Type),
                        meta => meta.ModelMeta.Name);

            // void UpdatePlayerInfo(PlayerInfo info)
            var updateModel =
                "void Update{0}({1})".Basic<FieldMeta>(meta => meta.ModelMeta.Name, meta => modelFieldPara.Code(meta.ModelMeta));

            // ISessionReader
            var readInterfaceName = "I{0}Reader".Basic<ModelMeta>(meta => meta.Name);
            // ISessionAccesser
            var accessInterfaceName = "I{0}Accesser".Basic<ModelMeta>(meta => meta.Name);

            // public interface ISessionReader : IDisposable{...}
            var readInterface = "public interface {0}: IDisposable"
                                .Basic<ModelMeta>(meta => readInterfaceName.Code(meta))
                                .Combine(
                                    Generator.GenSelect(loadModel.Satisfy(isKey), loadField)
                                    .Statement()
                                    .Many("\n")
                                    .Brace()
                                    , meta => meta.Fields);

            // public interface ISessionAccesser : ISessionReader, ISubmitChangable{...}
            var accessInterface = "public interface {0}: {1}, ISubmitChangable"
                                  .Basic<ModelMeta>(meta => accessInterfaceName.Code(meta), meta => readInterfaceName.Code(meta))
                                  .Combine(
                                      Generator.GenSelect(updateModel.Satisfy(isKey), updateField)
                                      .Statement()
                                      .Many("\n")
                                      .Brace()
                                      , meta => meta.Fields);

            var modelAllFields =
                "#region {0}".Basic<ModelMeta>(meta => meta.Name)
                .SkipLine()
                .Combine(
                    Generator.GenSelect(loadModel.Satisfy(isKey), loadField)
                    .Statement()
                    .Many("\n")
                    ,
                    meta => meta.Fields)
                .Combine(
                    Generator.GenSelect(updateModel.Satisfy(isKey), updateField)
                    .Statement()
                    .Many("\n")
                    ,
                    meta => meta.Fields)
                .SkipLine()
                .WithPostfix("#endregion\n");

            // var val = new PlayerInfo();
            // var val = default(UInt);
            var val =
                Generator.GenSelect(
                    "var val = default({0});".Basic<Type>(CoderHelper.GetTypeStr).Satisfy(meta => meta.IsPrimitive || meta == typeof(string) || meta.IsArray)
                    //, "byte[] val = null;".Unit<Type>().Satisfy(meta=>meta == typeof(byte[]))
                    , "var val = new {0}();".Basic<Type>(CoderHelper.GetTypeStr));

            // default(T) / null
            var failVal =
                Generator.GenSelect(
                    "default({0})".Basic<Type>(CoderHelper.GetTypeStr).Satisfy(meta => meta.IsPrimitive || meta == typeof(string) || meta.IsEnum)
                    , "null".Unit<Type>());

            // reader.ReadField<UInt64>("Pid");/(TowerState)reader.ReadField<Int32>("Test");
            var readField =
                Generator.GenSelect(
                    "({0})reader.ReadField<Int32>(\"{1}\");".Basic<FieldMeta>(meta => CoderHelper.GetTypeStr(meta.Type), meta => meta.Name).Satisfy(meta => meta.Type.IsEnum)
                    , "reader.ReadField<{0}>(\"{1}\");".Basic<FieldMeta>(meta => CoderHelper.GetTypeStr(meta.Type), meta => meta.Name));

            // val = reader.ReadField<UInt64>("Pid");
            var fieldReadField =
                "val = {0}".Basic<FieldMeta>(readField.Code);

            // val.Pid = reader.ReadField<UInt64>("Pid");
            var modelReadField =
                "val.{0} = {1}".Basic<FieldMeta>(meta => meta.Name, readField.Code);

            // val.Pid = (ulong)key;
            var modelReadKeyField =
                "val.{0} = ({1})key;".Basic<FieldMeta>(meta => meta.Name, meta => CoderHelper.GetTypeStr(meta.Type));

            var fieldReader =
                Generator.GenSelect(
                    "".Unit<FieldMeta>()
                    .Combine(Generator.GenSelect(modelReadKeyField.Satisfy(isKey), modelReadField).Many("\n"), meta => meta.ModelMeta.Fields)
                    .Satisfy(isKey)
                    , fieldReadField);

            var fieldModelGetter =
                "(Int32)".Unit<FieldMeta>().Satisfy(meta => meta.Type.IsEnum)
                .Append("{1}.{0}".Basic<FieldMeta>(meta => meta.Name, meta => meta.ModelMeta.Name.ToLower()));

            var fieldGetter =
                "(Int32)".Unit<FieldMeta>().Satisfy(meta => meta.Type.IsEnum)
                .Append("{0}".Basic<FieldMeta>(meta => meta.Name.ToLower()));

            // .WriteField("InfoSkill", infoskill)
            var writeField =
                ".WriteField(\"{0}\", {1})".Basic<FieldMeta>(meta => meta.Name, fieldGetter.Code);

            // .WriteField("{0}", ({1})key)
            var writeKey =
                ".WriteField(\"{0}\", ({1})key)".Basic<FieldMeta>(meta => meta.Name, meta=>CoderHelper.GetTypeStr(meta.Type));

            // .WriteField("Pid", playerinfo.Pid)
            var writeFieldModel =
                ".WriteField(\"{0}\", {1})".Basic<FieldMeta>(meta => meta.Name, fieldModelGetter.Code);

            Func<bool, ICoder<FieldMeta>> writer =
                isRedis =>
                {
                    return "var ctx =  adaptor.Update(dataId);\n\nctx".Unit<FieldMeta>()
                    .SkipLine()
                    .Append(Generator.GenSelect(
                        "".Unit<FieldMeta>().Combine(Generator.GenSelect(
                        writeFieldModel.Satisfy(isKey), writeFieldModel).Many("\n"),
                    meta => meta.ModelMeta.Fields).Satisfy(isKey)
                        ,
                        "{0}".Basic<FieldMeta>(meta => writeKey.Code(meta.ModelMeta.Key))
                        .SkipLine().Satisfy(meta => !isRedis)
                        .Append(writeField)))
                    .SkipLine()
                    .Append(
                        ";\nupdateQueue.Add(ctx);".Unit<FieldMeta>());
                };

            var updateSig = Generator.GenSelect(updateModel.Satisfy(isKey), updateField).WithPrefix("public ");
            var loadSig = Generator.GenSelect(loadModel.Satisfy(isKey), loadField).WithPrefix("public async ");

            #region redis

            // var dataId = string.Format("player:{0}", id);
            var key =
                "var dataId = string.Format(\"{0}:{{0}}\", {1});".Basic<FieldMeta>(
                    meta => meta.ModelMeta.RedisCacheName, meta => meta.ModelMeta.Key.Name.ToLower());

            var redisReader =
                Generator.GenSelect(
                    "var reader = await adaptor.QueryAll(dataId);if (reader == null){{return {0};}}".Basic<FieldMeta>(meta => failVal.Code(meta.ModelMeta.Type))
                    .Satisfy(isKey)
                    ,
                    "var reader = await adaptor.Query(dataId, \"{0}\");if (reader == null){{return {1};}}"
                    .Basic<FieldMeta>(meta => meta.Name, meta => failVal.Code(meta.Type)));

            var redisUpdateLast =
                "return await adaptor.UpdateWork({0});".Basic<FieldMeta>(meta => meta.ModelMeta.Key.Name.ToLower());

            var redisUpdate =
                updateSig.Append(
                    writer(true)/*.Append(redisUpdateLast)*/.Brace());

            var redisLoad =
                loadSig.Append(
                    Generator.GenSelect(
                        "{0}".Basic<FieldMeta>(meta => val.Code(meta.ModelMeta.Type)).Satisfy(isKey)
                        , "".Unit<FieldMeta>().Combine(val, meta => meta.Type)).Append(redisReader).Append(fieldReader).WithPostfix("return val;").Brace());

            var interfaceInherit =
                accessInterfaceName.Many(",");

            var delegateInnerRedisSpecial =
                @"public async Task<bool> SubmitChanges()
            {
                await adaptor.SubmitChanges(
        new[] { dataId }
      , new[] { contextLockId }
      , new [] {updateQueue});

                return true;
            }

            public async Task<bool> SubmitChangesWith(params object[] others)
            {
                await adaptor.SubmitChanges(
                    new[] { dataId } .Concat(others.Select(d => ((DelegateBase)d).dataId)).ToArray(),
                    new[] { contextLockId } .Concat(others.Select(d => ((DelegateBase)d).contextLockId)).ToArray(),
                    new[] { updateQueue } .Concat(others.Select(d => ((DelegateBase)d).updateQueue)).ToArray());

                return true;
            }

            public void Dispose()
            {

            }
".Unit();

            var redisDelegateInner = 
@"class Delegate<TKey> : DelegateBase, {0}
{{            
public Delegate(RedisAdaptor adaptor, long contextLockId, TKey key, string dataId)
            {{
                this.adaptor = adaptor;
                this.contextLockId = contextLockId;
                this.key = key;
                this.dataId = dataId;
            }}
            {2}
            private readonly RedisAdaptor adaptor;
            private readonly object key;

            {1}
}}"
                                     .Basic<IEnumerable<ModelMeta>>(
                                         interfaceInherit.Code,
                                         "#region {0}\n{1}\n#endregion\n".Basic<ModelMeta>(meta => meta.Name,
                                                 meta => redisUpdate.Many("\n").SkipLine().Append(redisLoad.Many("\n")).Code(meta.Fields)).Many("\n").Code, delegateInnerRedisSpecial.Code);

            var modelReaderGetter =
                "public async Task<I{0}Reader> Get{0}Reader({1} {2}){{var dataId = string.Format(\"{3}:{{0}}\", {2});var lockId = 0;return new Delegate<{1}>(adaptor, lockId, {2}, dataId);}}"
                     .Basic<ModelMeta>(
                     meta => meta.Name
                    , meta => CoderHelper.GetTypeStr(meta.Key.Type)
                    , meta => meta.Key.Name.ToLower()
                    , meta => meta.Name.ToLower());
            var modelAccesserGetter =
    "public async Task<I{0}Accesser> Get{0}Accesser({1} {2}){{var dataId = string.Format(\"{3}:{{0}}\", {2});var lockId = await adaptor.LockKey(dataId);return new Delegate<{1}>(adaptor, lockId, {2}, dataId);}}"
         .Basic<ModelMeta>(
         meta => meta.Name
        , meta => CoderHelper.GetTypeStr(meta.Key.Type)
        , meta => meta.Key.Name.ToLower()
        , meta => meta.Name.ToLower());

            var redisDelegate = "public class RedisDataServiceDelegate {{\n{0}\n{1}\n\nprivate readonly RedisAdaptor adaptor;public RedisDataServiceDelegate(RedisAdaptor adaptor){{this.adaptor = adaptor;}}\n }}"
                .Basic<IEnumerable<ModelMeta>>(
                    redisDelegateInner.Code
                    , modelReaderGetter.SkipLine().Append(modelAccesserGetter).Many("\n").Code);

            #endregion

            #region mysql

            // `Pid`, `Uid`, `Name`, `Level`, `InfoSkill` ,`InfoItem`
            var fields =
                Generator.GenSelect(
                    "".Unit<FieldMeta>()
                    .Combine("`{0}`".Basic<FieldMeta>(meta => meta.Name).Many(","),
                             meta => meta.ModelMeta.Fields)
                    .Satisfy(meta => meta.ModelMeta.Key == meta)
                    , "`{0}`".Basic<FieldMeta>(meta => meta.Name));

            // const string sql = "SELECT `Pid`, `Uid`, `Name`, `Level`, `InfoSkill` ,`InfoItem` FROM `mem_player` WHERE `Pid` = ?1";
            var querySql =
                "const string sql = \"SELECT {0} FROM `{1}` WHERE `{2}` = ?1\";"
                .Basic<FieldMeta>(fields.Code, meta => meta.ModelMeta.MysqlTableName,
                                  meta => meta.ModelMeta.Key.Name);

            var mysqlReader =
                Generator.GenSelect(
                    "var reader = await adaptor.Query(sql, ({0})key);if (reader == null){{return {1};}}"
                    .Basic<FieldMeta>(meta => CoderHelper.GetTypeStr(meta.ModelMeta.Key.Type), meta => failVal.Code(meta.ModelMeta.Type))
                    .Satisfy(isKey)
                    ,
                    "var reader = await adaptor.Query(sql, ({0})key);if (reader == null){{return {1};}}"
                    .Basic<FieldMeta>(meta => CoderHelper.GetTypeStr(meta.ModelMeta.Key.Type), meta => failVal.Code(meta.Type)));

            var updateSqlKeyString =
                "INSERT INTO `{0}`({1}) VALUES({2}) ON DUPLICATE KEY UPDATE {3}".Basic<FieldMeta>(
                    meta => meta.ModelMeta.MysqlTableName
                    ,
                    "".Unit<FieldMeta>()
                    .Combine("`{0}`".Basic<FieldMeta>(meta => meta.Name).Many(","),
                             meta => meta.ModelMeta.Fields)
                    .Code
                    ,
                    "".Unit<FieldMeta>()
                    .Combine("?{0}".Basic<FieldMeta>(meta => meta.Position).Many(","),
                             meta => meta.ModelMeta.Fields)
                    .Code
                    ,
                    "".Unit<FieldMeta>()
                    .Combine(
                        "`{0}`=?{1}".Basic<FieldMeta>(meta => meta.Name, meta => meta.Position)
                        .Many(",", meta => !isKey(meta)), meta => meta.ModelMeta.Fields)
                    .Code);

            var updateSqlFieldString =
                "UPDATE `{0}` SET `{1}`=?2 WHERE `{2}`=?1".Basic<FieldMeta>(
                    meta => meta.ModelMeta.MysqlTableName, meta => meta.Name, meta => meta.ModelMeta.Key.Name);

            var updateSql =
                "const string dataId = \"{0}\"; ".Basic<FieldMeta>(Generator.GenSelect(
                            updateSqlKeyString.Satisfy(isKey), updateSqlFieldString).Code);

            var mysqlUpdate =
                updateSig.Append(
                    Generator.GenSelect(
                        updateSql.Append(writer(false)).Satisfy(meta => meta.ModelMeta.MysqlTableName != null)
                        , "throw new Exception();".Unit<FieldMeta>())
                    .Brace());

            var mysqlLoad =
                loadSig.Append(
                    Generator.GenSelect(
                        querySql.Append(Generator.GenSelect(
                                            "{0}".Basic<FieldMeta>(meta => val.Code(meta.ModelMeta.Type)).Satisfy(isKey)
                                            , "".Unit<FieldMeta>().Combine(val, meta => meta.Type))).Append(mysqlReader).Append(fieldReader).WithPostfix("reader.Dispose();\nreturn val;").Satisfy(meta => meta.ModelMeta.MysqlTableName != null)
                        , "throw new Exception();".Unit<FieldMeta>())
                    .Brace());
            var delegateInnerMysqlSpecial =
                @"            public Task<bool> SubmitChanges()
            {
            throw new NotImplementedException();
            }

            public Task<bool> SubmitChangesWith(params object[] others)
            {
            throw new NotImplementedException();
            }

            public void Dispose()
            {

            }
".Unit();

            var mysqlDelegateInner =
@"class Delegate<TKey> : {0}
{{            
public Delegate(MysqlAdaptor adaptor, TKey key)
            {{
                this.adaptor = adaptor;
                this.key = key;
            }}
            {2}
            private readonly MysqlAdaptor adaptor;
            private readonly object key;
            private readonly List<IUpdateDataContext> updateQueue = new List<IUpdateDataContext>();

            {1}
}}"
                         .Basic<IEnumerable<ModelMeta>>(
                             accessInterfaceName.Many(",", m => m.MysqlTableName != null).Code,
                             "#region {0}\n{1}\n#endregion\n".Basic<ModelMeta>(meta => meta.Name,
                                     meta => mysqlUpdate.Many("\n").SkipLine().Append(mysqlLoad.Many("\n")).Code(meta.Fields)).Many("\n", m=>m.MysqlTableName!=null).Code, delegateInnerMysqlSpecial.Code);

            Func<string, ICoder<ModelMeta>> mysqlModelGetter = interfaceType =>
            {
                return string.Format("public async Task<I{{0}}{0}> Get{{0}}{0}({{1}} {{2}}){{{{return new Delegate<{{1}}>(adaptor, {{2}});}}}}"
                , interfaceType)
                .Basic<ModelMeta>(
                     meta => meta.Name
                    , meta => CoderHelper.GetTypeStr(meta.Key.Type)
                    , meta => meta.Key.Name.ToLower()
                    , meta => meta.Name.ToLower());
            };

            var mysqlModelReaderGetter = mysqlModelGetter("Reader");
            var mysqlModelAccesserGetter = mysqlModelGetter("Accesser");

            var mysqlDelegate = "public class MysqlDataServiceDelegate {{{0}\n{1}\n\nprivate readonly MysqlAdaptor adaptor;public MysqlDataServiceDelegate(MysqlAdaptor adaptor){{this.adaptor = adaptor;}}\n }}"
    .Basic<IEnumerable<ModelMeta>>(
        mysqlDelegateInner.Code
        , mysqlModelReaderGetter.SkipLine().Append(mysqlModelAccesserGetter).Many("\n", m => m.MysqlTableName != null).Code);
            #endregion

            var genCoder =
                readInterface.SkipLine().Append(accessInterface).Many("\n")
                .SkipLine(2)
                .Append(redisDelegate)
                .SkipLine(2)
                .Append(mysqlDelegate);

            return genCoder;
        }

        private readonly ICoder<IEnumerable<Type>> dumpCoder =
            "public enum {0}".Basic<Type>(CoderHelper.GetTypeStr).Satisfy(meta => meta.IsEnum)
            .Combine(
                "{0} = {1},".Basic<FieldInfo>(meta => meta.Name, meta => (int)meta.GetValue(null)).Many("\n", meta => meta.FieldType.IsEnum).WithPrefix("\n").WithPostfix("\n").Brace(), type => type.GetFields())
            .Satisfy(meta => meta.IsEnum).Many("\n");

        public string GenClientSerialize(IEnumerable<ModelMeta> ast, IEnumerable<Type> types)
        {
            var topTemplate = ResourceLoader.Load("AutoClientTemplate.txt");
            var coder = GenSerializeCoder(true);

            return string.Format(topTemplate, dumpCoder.Code(types) + "\n" + coder.Code(ast), GenModelVersion(ast));
        }

        public string GenServerSerialize(IEnumerable<ModelMeta> ast, IEnumerable<Type> types)
        {
            var topTemplate = ResourceLoader.Load("AutoServerTemplate.txt");
            var coder = GenSerializeCoder(false);

            return string.Format(topTemplate, dumpCoder.Code(types) + "\n" + coder.Code(ast), GenModelVersion(ast));
        }

        public string GenDataAccess(IEnumerable<ModelMeta> ast)
        {
            var topTemplate = ResourceLoader.Load("AutoDataAccessTemplate.txt");
            var coder = GenDataAccessCoder();

            return string.Format(topTemplate, coder.Code(ast));
        }

        public string GenModelVersion(IEnumerable<ModelMeta> ast)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            foreach (var modelMeta in ast.OrderBy(m=>m.Name))
            {
                var stream = modelMeta.GetVersionBytes();

                bw.Write(stream.GetBuffer(), 0, (int)stream.Length);
            }

            return string.Join("", MD5.Create().ComputeHash(ms.GetBuffer()).Select(b=>b.ToString("x").PadLeft(2, '0')));
        }
    }
}
