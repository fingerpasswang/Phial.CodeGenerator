using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CodeC;

namespace ServiceCodeC
{
    class ServiceGenerator
    {
        static ICoder<IEnumerable<ServiceMeta>> GenCoder(bool client)
        {
            #region name related
            // DbSyncNotify/LogicClient
            var serviceName = Generator.GenSelect(
                                  "{0}Notify".Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Type == ServiceType.Notify)
                                  , "{0}".Basic<ServiceMeta>(meta => meta.Name));

            // IDbSyncNotifyImpl
            var implName =
                "I{0}Impl".Basic<ServiceMeta>(meta => serviceName.Code(meta));

            // ILogicClientInvoke
            var invokeName =
                "I{0}Invoke".Basic<ServiceMeta>(meta => serviceName.Code(meta));

            // IClientLogicService
            var trivialServiceName =
                "I{0}Service".Basic<ServiceMeta>(meta => serviceName.Code(meta));

            // DbSyncNotifyDelegate/LoginNotifyDelegate
            var delegateName =
                Generator.GenSelect(
                    "{0}NotifyDelegate".Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Type == ServiceType.Notify)
                    , "{0}ServiceDelegate".Basic<ServiceMeta>(meta => meta.Name));

            // LoginNotifySerializer
            var serializerName =
                "{0}Serializer".Basic<ServiceMeta>(meta => serviceName.Code(meta));

            // LoginClientDispatcher
            var dispatcherName =
                "{0}Dispatcher".Basic<ServiceMeta>(meta => serviceName.Code(meta));

            // Task/Task<bool>
            var taskTypeName = Generator.GenSelect(
                                   "Task"
                                   .Unit<Type>()
                                   .Satisfy(meta => meta.Name.ToLower().Equals("void")),
                                   "Task<{0}>"
                                   .Basic<Type>(CoderHelper.GetTypeStr));

            // InvokeOperation/InvokeOperation<bool>
            var invokeTypeName = Generator.GenSelect(
                                     "InvokeOperation"
                                     .Unit<Type>()
                                     .Satisfy(meta => meta.Name.ToLower().Equals("void")),
                                     "InvokeOperation<{0}>"
                                     .Basic<Type>(CoderHelper.GetTypeStr));

            // InvokeOperation/InvokeOperation<bool>
            var invokeCallName = Generator.GenSelect(
                                     "Invoke"
                                     .Unit<Type>()
                                     .Satisfy(meta => meta.Name.ToLower().Equals("void")),
                                     "Invoke<{0}>"
                                     .Basic<Type>(CoderHelper.GetTypeStr));

            var invokeTaskCallName = Generator.GenSelect(
                                     "InvokeT"
                                     .Unit<Type>()
                                     .Satisfy(meta => meta.Name.ToLower().Equals("void")),
                                     "InvokeT<{0}>"
                                     .Basic<Type>(CoderHelper.GetTypeStr));
            #endregion

            #region function related
            // ulong pid
            var paraSig =
                "{0} {1}".Basic<ParameterMeta>(meta => CoderHelper.GetTypeStr(meta.Type), meta => meta.Name);
            // ulong pid, string newName
            var parasSig = Generator.GenSelect(
                               paraSig.Many(",").Satisfy(meta => meta.Any())
                               , "".Unit<IEnumerable<ParameterMeta>>().Satisfy(meta => !meta.Any()));

            // SyncPositionMulticast(int groupId, Int32 x, Int32 y)
            var methodMulticast = " {0}(int groupId, {1});".Basic<MethodMeta>(meta => meta.Name,
                                          meta => parasSig.Code(meta.Parameters));

            // AskChangeName(UInt64 pid, String newName);
            var methodWithoutReturnType = " {0}({1});".Basic<MethodMeta>(meta => meta.Name,
                                          meta => parasSig.Code(meta.Parameters));

            // Task<bool> AskChangeName(ulong pid, string newName);
            var taskMethod =
                methodWithoutReturnType.CombineReverse(taskTypeName, meta => meta.ReturnType);

            // bool AskChangeName(ulong pid, string newName);
            var normalMethod =
                methodWithoutReturnType.CombineReverse(Generator.GenBasic<Type>(CoderHelper.GetTypeStr), meta => meta.ReturnType);

            // InvokeOperation<bool> AskChangeName(ulong pid, string newName);
            var invokeMethod =
                methodWithoutReturnType.CombineReverse(invokeTypeName, meta => meta.ReturnType);
            #endregion

            #region interface related
            // public interface ILogicClientInvoke{...}
            var invokeGenCoder = Generator.GenCombine(invokeName.WithPrefix("public interface "),
                                 invokeMethod.Many("\n").Brace(), meta => meta.Methods).Satisfy(meta => meta.Scope == ServiceScope.ClientToServer);

            // public interface ILoginNotifyImpl{...}
            var serverImplGenCoder =
                implName
                .WithPrefix("public interface ")
                .WithPostfix(": IRpcImplInstnce")
                .Combine(taskMethod.Many("\n").Brace(), meta => meta.Methods)
                .Satisfy(meta => meta.Scope != ServiceScope.ServerToClient);

            // public interface IClientLoginImpl{...}
            var clientImplGenCoder =
                implName
                .WithPrefix("public interface ")
                .WithPostfix(": IRpcImplInstnce")
                .Combine(normalMethod.Many("\n").Brace(), meta => meta.Methods)
                .Satisfy(meta => meta.Scope == ServiceScope.ServerToClient);

            #endregion

            #region delegate

            #region meta data
            // public const uint NotifyLogicServerWorking = 2001;
            var methodId =
                "public const uint {0} = {1};".Basic<MethodMeta>(meta => meta.Name, meta => meta.Id);

            // #region meta data ... #endregion
            var metaGenCoder = methodId.Many("\n").Brace().WithPrefix("private static class MethodId").Region("meta data");
            #endregion

            #region constructor && forward
            //private readonly ServiceDelegateStub serviceDelegateStub;
            //public ClientLogicServiceDelegate(IDataSender dataSender)
            //{
            //    serviceDelegateStub = new ServiceDelegateStub(dataSender, ClientLogicSerializer.Instance, MetaData.GetServiceRoutingRule(AutoInit.ClientLogic));
            //    dataSender.RegisterDelegate(serviceDelegateStub, AutoInit.ClientLogic);
            //}
            var delegateConstructor =
                @"private readonly ServiceDelegateStub serviceDelegateStub;
                  public {0}(IDataSender dataSender)
                  {{serviceDelegateStub = new ServiceDelegateStub(dataSender, {1}.Instance, MetaData.GetServiceRoutingRule(AutoInit.{2}));
                    dataSender.RegisterDelegate(serviceDelegateStub, AutoInit.{2});}}
"
                .Basic<ServiceMeta>(meta => delegateName.Code(meta), meta => serializerName.Code(meta), meta => meta.Name);

            //private readonly string forwardKey;
            //private ClientLoginServiceDelegate(ServiceDelegateStub serviceDelegateStub, string forwardKey)
            //{
            //    this.forwardKey = forwardKey;
            //    this.serviceDelegateStub = serviceDelegateStub;
            //}
            //public ClientLoginServiceDelegate Forward(byte[] sessionId)
            //{
            //    return new ClientLoginServiceDelegate(serviceDelegateStub, new Guid(sessionId).ToString());
            //}
            var delegateForward =
                @"private readonly byte[] forwardKey;
                  private {0}(ServiceDelegateStub serviceDelegateStub, byte[] forwardKey)
                  {{this.forwardKey = forwardKey;this.serviceDelegateStub = serviceDelegateStub;}}
                  public {0} Forward(byte[] forwardId)
                  {{return new {0}(serviceDelegateStub, forwardId);}}
"
                .Basic<ServiceMeta>(
                    meta => delegateName.Code(meta)).Satisfy(meta => meta.Scope == ServiceScope.ServerToClient);

            var forwardKey =
                Generator.GenSelect(
                    "forwardKey".Unit<ServiceMeta>().Satisfy(meta => meta.Scope == ServiceScope.ServerToClient)
                    , "null".Unit());
            #endregion

            // notify delegate

            // public void ServerMessageOk()
            var invokeMethodServerSig =
                "public {0} {1}({2})".Basic<MethodMeta>(meta =>
                {
                    if (meta.ReturnType == typeof(void))
                    {
                        return "void";
                    }
                    else
                    {
                        return "Task<" + CoderHelper.GetTypeStr(meta.ReturnType) + ">";
                    }
                    
                }, meta => meta.Name,
                        meta => parasSig.Code(meta.Parameters));

            // public InvokeOperation<Boolean> AskLogin(UInt64 pid)
            var invokeMethodClientSig =
                "public {0} {1}({2})".Basic<MethodMeta>(meta => invokeTypeName.Code(meta.ReturnType), meta => meta.Name,
                        meta => parasSig.Code(meta.Parameters));

            // pid, newName
            var parasCall =
                "{0}".Basic<ParameterMeta>(meta => meta.Name).Many(",");

            var comma = Generator.GenSelect(
                            ",".Unit<List<ParameterMeta>>().Satisfy(meta1 => meta1.Count > 0)
                            , "".Unit<List<ParameterMeta>>().Satisfy(meta1 => meta1.Count == 0));

            // MethodId.AskChangeName, forwardKey, pid, newName
            var invokeCall =
                "MethodId.{0}, {1}{2}{3}".Basic<MethodMeta>(meta => meta.Name, meta => forwardKey.Code(meta.ServiceMeta), meta => comma.Code(meta.Parameters),
                        meta => parasCall.Code(meta.Parameters));

            // server to client delegate && notify delegate
            // serviceDelegateStub.Notify(MethodId.NotifyLogicServerWorking, null, districts);
            var invokeNoReturnBody = Generator.GenSelect(
                "serviceDelegateStub.Notify{0};".Basic<MethodMeta>(meta => invokeCall.Bracket().Code(meta)).Satisfy(
                        meta => meta.ServiceMeta.Type == ServiceType.Notify || meta.ServiceMeta.Type == ServiceType.Sync),
                "return serviceDelegateStub.{0}{1};".Basic<MethodMeta>(meta => invokeTaskCallName.Code(meta.ReturnType),
                        meta => invokeCall.Bracket().Code(meta))
                );

            var multicastDelegate =
                "public void {0}Multicast(int groupId{1} {2})"
                    .Basic<MethodMeta>(meta => meta.Name, meta => comma.Code(meta.Parameters),
                        meta => parasSig.Code(meta.Parameters))
                    .Append(
                        "serviceDelegateStub.Multicast(MethodId.{0}, groupId{1} {2});"
                            .Basic<MethodMeta>(meta => meta.Name, meta => comma.Code(meta.Parameters),
                                meta => parasCall.Code(meta.Parameters)).Brace());

            // return serviceDelegateStub.Invoke<Boolean>(MethodId.AskAddMoney, null, pid, money);
            var invokeClientBody =
                "return serviceDelegateStub.{0}{1};".Basic<MethodMeta>(meta => invokeCallName.Code(meta.ReturnType),
                        meta => invokeCall.Bracket().Code(meta));


            //public void NotifyPlayerLoaded(UInt64 pid){this.Task(methodNotifyPlayerLoaded, pid);}
            var invokeNoReturnMethodGen = Generator.GenFunction(invokeMethodServerSig, invokeNoReturnBody, Generator.Id);
            var invokeClientMethodGen = Generator.GenFunction(invokeMethodClientSig, invokeClientBody, Generator.Id);

            var delegateMethod =
                Generator.GenSelect(
                    invokeClientMethodGen.Satisfy(
                        meta => meta.ServiceMeta.Scope == ServiceScope.ClientToServer && client)
                    , invokeNoReturnMethodGen);

            var delegateCoder =
                "public class {0}".Basic<ServiceMeta>(meta => delegateName.Code(meta))
                .Append(": {0}".Basic<ServiceMeta>(meta => invokeName.Code(meta))
                        .Satisfy(meta => meta.Scope == ServiceScope.ClientToServer && client))
                .Append(delegateConstructor.Append(delegateForward)
                        .Combine(metaGenCoder, meta => meta.Methods)
                        .Combine(delegateMethod.Many("\n"), meta => meta.Methods)
                        .Combine(multicastDelegate.Satisfy(meta=>meta.ServiceMeta.Multicast&&!client).Many("\n"), meta => meta.Methods).Brace());

            #endregion

            // case 1001:
            var label = "case {0}:".Basic<MethodMeta>(meta => meta.Id);

            #region dispatcher

            // (ulong)method.Args[0]
            var unboxedArg =
                "({0})(method.Args[{1}])".Basic<ParameterMeta>(meta => CoderHelper.GetTypeStr(meta.Type),
                        meta => meta.Position);

            // (UInt64)method.Args[0], (String)method.Args[1]
            var unboxedArgs = unboxedArg.Many(",");

            // AskChangeName((UInt64)method.Args[0], (String)method.Args[1])
            var dispatchMethod =
                "{0}".Basic<MethodMeta>(meta => meta.Name).Combine(unboxedArgs.Bracket(), meta => meta.Parameters);

            // public static readonly ClientLogicDispatcher Instance = new ClientLogicDispatcher();
            var dispatcherSingleton =
                "public static readonly {0} Instance = new {0}();"
                .Basic<ServiceMeta>(meta => dispatcherName.Code(meta));

            // (IDbSyncNotifyImpl)impl
            var implConvert =
                "({0})impl".Basic<ServiceMeta>(meta => implName.Code(meta));

            // ((IDbSyncNotifyImpl)impl).NotifyPlayerLoaded((UInt64)method.Args[0])
            var dispatchCall =
                "({0}).{1}".Basic<MethodMeta>(meta => implConvert.Code(meta.ServiceMeta),
                                              meta => dispatchMethod.Code(meta));

            //  case 4001:
            //  ((ILoginClientImpl)impl).AskLogin((string)method.Args[0], (byte[])method.Args[1]).ContinueWith(t => DoContinue(t, cont));
            //  break;
            //  case 5001:
            //  ((IClientLogicImpl)impl).ServerMessageOk();
            //  break;
            var dispatchSwitch =
                label.Append(
                    Generator.GenSelect(
                        "{0}.ContinueWith(t=>DoContinue(t, cont));".Basic<MethodMeta>(meta => dispatchCall.Code(meta)).Satisfy(meta => meta.ServiceMeta.Scope != ServiceScope.ServerToClient && meta.ServiceMeta.Type != ServiceType.Notify)
                        , "{0};".Basic<MethodMeta>(meta => dispatchCall.Code(meta))))
                .Append("break;".Unit<MethodMeta>());

            var dispatchMethodAll =
                "public void Dispatch(IRpcImplInstnce impl, RpcMethod method, ServiceImplementStub.SendResult cont)"
                .Unit<ServiceMeta>()
                .Append(
                    "switch (method.MethodId)"
                    .Unit<ServiceMeta>()
                    .Combine(
                        dispatchSwitch.Many("\n").Brace(), meta => meta.Methods)
                    .Brace());

            var dispatcherCoder =
                "public class {0} : "
                .Basic<ServiceMeta>(meta => dispatcherName.Code(meta))
                .Append("ServiceMethodDispatcherEx, ".Unit<ServiceMeta>().Satisfy(meta => !client))
                .Append("IServiceMethodDispatcher".Unit<ServiceMeta>())
                .Append(
                    dispatcherSingleton.Append(dispatchMethodAll).Brace());

            #endregion

            #region serializer

            // public static readonly ClientLogicSerializer Instance = new ClientLogicSerializer();
            var serializerSingleton =
                "public static readonly {0} Instance = new {0}();"
                .Basic<ServiceMeta>(meta => serializerName.Code(meta));

            var methodArgsGetter = "method.Args[{0}]".Basic<ParameterMeta>(meta => meta.Position);
            var argsGetter = "args[{0}]".Basic<ParameterMeta>(meta => meta.Position, meta=>CoderHelper.GetTypeStr(meta.Type));

            var readArgCheck =
                CoderHelper.ReadWithCheckCoder<ParameterMeta>()
                    .Lift(
                        (ParameterMeta meta) =>
                            new Tuple<Type, ICoder<ParameterMeta>, ParameterMeta>(meta.Type, methodArgsGetter, meta));

            var writeArgCheck =
                CoderHelper.WriteWithCheckCoder<ParameterMeta>()
                    .Lift(
                        (ParameterMeta meta) =>
                            new Tuple<Type, ICoder<ParameterMeta>, ParameterMeta>(meta.Type, argsGetter, meta));

            var readSwitch =
                label
                .Append(
                    "method.Args = new object[{0}];".Basic<MethodMeta>(meta => meta.Parameters.Count).Satisfy(meta => meta.Parameters.Count > 0)
                    .Combine(readArgCheck.Many("\n"), meta => meta.Parameters))
                //.Append(
                //    "method.NeedReturn = true;"
                //    .Unit<MethodMeta>()
                //    .Satisfy(meta => meta.ServiceMeta.Type == ServiceType.Service))
                .Append(
                    "break;".Unit<MethodMeta>());

            var readDispatchGen =
                "public RpcMethod Read(BinaryReader br)".Unit<ServiceMeta>()
                .Append(
                    "RpcMethod method = new RpcMethod();\nmethod.MethodId = br.ReadUInt32();\n".Unit<ServiceMeta>()
                    .Append(
                        "switch (method.MethodId)".Unit<ServiceMeta>()
                        .Combine(
                            readSwitch.Many("\n").Brace(), meta => meta.Methods))
                    .Append(
                        "return method;".Unit<ServiceMeta>()).Brace());

            var writeSwitch =
                label
                .Combine(
                    writeArgCheck.Many("\n").WithPostfix("break;").Brace(), meta => meta.Parameters);

            var writeDispatchGen =
                "public void Write(uint methodId, object[] args, BinaryWriter bw)"
                .Unit<ServiceMeta>()
                .Append(
                    "bw.Write(methodId);\n"
                    .Unit<ServiceMeta>()
                    .Append("switch (methodId)"
                            .Unit<ServiceMeta>()
                            .Combine(
                                writeSwitch.Many("\n").Brace(), meta => meta.Methods)).Brace());

            var readReturnWithCheck =
    CoderHelper.ReadWithCheckCoder<MethodMeta>()
        .Lift(
            (MethodMeta meta) =>
                new Tuple<Type, ICoder<MethodMeta>, MethodMeta>(meta.ReturnType, "returnVal".Unit<MethodMeta>(), meta));

            var readReturnSwitch =
                label
                .Append(readReturnWithCheck.WithPostfix("break;").Brace()).Satisfy(meta => meta.ReturnType != typeof(void));

            var readReturnDispatch =
                "public object ReadReturn(uint methodId, BinaryReader br)".Unit<ServiceMeta>()
                    .Append(
                        (
                            ("var returnVal = new object();\n").Unit<ServiceMeta>()
                                .Append(
                                    "switch (methodId)".Unit<ServiceMeta>()
                                        .Combine(readReturnSwitch.Many("\n").Brace(), meta => meta.Methods)
                                        .Satisfy(meta => meta.Methods.Any(m => m.ReturnType != typeof (void))))
                                .Append("return returnVal;".Unit<ServiceMeta>())
                                .Brace()
                            ));

            var writeValueWithCheck =
                CoderHelper.WriteWithCheckCoder<MethodMeta>()
                    .Lift(
                        (MethodMeta meta) =>
                            new Tuple<Type, ICoder<MethodMeta>, MethodMeta>(meta.ReturnType, "value".Unit<MethodMeta>(), meta));

            var writeReturnSwitch =
                label
                .Append(writeValueWithCheck)
                .Append("break;".Unit<MethodMeta>())
                .Satisfy(meta => meta.ReturnType != typeof(void));

            var writeReturnDispatch =
                "public void WriteReturn(RpcMethod method, BinaryWriter bw, object value)".Unit<ServiceMeta>()
                .Append((
                    "switch (method.MethodId)".Unit<ServiceMeta>()
                    .Combine(
                        writeReturnSwitch.Many("\n").Brace(), meta => meta.Methods)).Satisfy(meta=>meta.Methods.Any(m=>m.ReturnType != typeof(void))).Brace());

            var serializerCoder =
                "public class {0} : IMethodSerializer".Basic<ServiceMeta>(meta => serializerName.Code(meta))
                .Append(
                    serializerSingleton
                    .Append(readDispatchGen.SkipLine())
                    .Append(writeDispatchGen.SkipLine())
                    .Append(readReturnDispatch.SkipLine())
                    .Append(writeReturnDispatch.SkipLine())
                    .Brace());
            #endregion

            #region serviceMeta

            var implMetaInit =
                @"MetaData.SetServiceId(typeof({0}), {1});
            MetaData.SetMethodSerializer(typeof({0}), {2}.Instance);
            MetaData.SetServiceMethodDispatcher(typeof({0}), {3}.Instance); "
                .Basic<ServiceMeta>(
                    meta => implName.Code(meta)
                    , meta => meta.Name
                    , meta => serializerName.Code(meta)
                    , meta => dispatcherName.Code(meta))
                .Satisfy(meta => (meta.Scope == ServiceScope.ServerToClient && client) || (meta.Scope != ServiceScope.ServerToClient && !client));

            var autoInitName =
                "internal const string {0} = \"{0}\";".Basic<ServiceMeta>(meta => meta.Name);

            var implBindingKey =
                Generator.GenSelect(
                    "ImplBindingKey = (districts, uuid) => \"padding.{0}.notify\",\n".Basic<ServiceMeta>(
                        meta => meta.Name)
                        .Satisfy(meta => meta.Type == ServiceType.Notify)
                    ,
                    "ImplBindingKey = (districts, uuid) => string.Format(\"{{0}}/{0}/sync/{{1}}\", districts, uuid),\n"
                        .Basic<ServiceMeta>(meta => meta.Name)
                        .Satisfy(
                            meta => meta.Type == ServiceType.Sync && meta.Scope == ServiceScope.ServerToClient && client)
                    ,
                    Generator.GenSelect(
                        "ImplBindingKey = (districts, uuid) => string.Format(\"{{0}}.{0}.invoke\", districts),\n"
                            .Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Divisional)
                        ,
                        "ImplBindingKey = (districts, uuid) => string.Format(\"padding.{0}.invoke\"),\n"
                            .Basic<ServiceMeta>(meta => meta.Name))
                        .Satisfy(meta => meta.Scope == ServiceScope.ClientToServer && !client)

                    ,
                    Generator.GenSelect(
                        Generator.GenSelect(
                            "ImplBindingKey = (districts, uuid) => string.Format(\"{{0}}.{0}.invoke\", districts),\n".Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta=>meta.Divisional)
                            , "ImplBindingKey = (districts, uuid) => string.Format(\"padding.{0}.invoke\", districts),\n".Basic<ServiceMeta>(meta => meta.Name)
                            ).Satisfy(meta => meta.Type == ServiceType.Service))
                        .Satisfy(meta => meta.Scope == ServiceScope.InterServer)

                    );

            var delegateRoutingKey =
                Generator.GenSelect(
                    "DelegateRoutingKey = (districts, fid) => \"padding.{0}.notify\",\n".Basic<ServiceMeta>(meta => meta.Name)
                    .Satisfy(meta => meta.Type == ServiceType.Notify)
                    ,
                    "DelegateRoutingKey = (districts, uuid) => string.Format(\"{{0}}.{0}.sync.{{1}}\", districts, uuid),\n".Basic<ServiceMeta>(meta => meta.Name)
                    .Satisfy(meta => meta.Type == ServiceType.Sync && meta.Scope == ServiceScope.ServerToClient && !client)
                    ,
                    Generator.GenSelect(
                        "DelegateRoutingKey = (districts, uuid) => string.Format(\"{{0}}/{0}/invoke\", districts),\n".Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Divisional)
                        ,"DelegateRoutingKey = (districts, uuid) => string.Format(\"padding/{0}/invoke\"),\n".Basic<ServiceMeta>(meta => meta.Name))
                    .Satisfy(meta => meta.Scope == ServiceScope.ClientToServer && client)
                    , Generator.GenSelect(
                        Generator.GenSelect(
                            "DelegateRoutingKey = (districts, uuid) => string.Format(\"{{0}}.{0}.invoke\", districts),\n".Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Divisional)
                            , "DelegateRoutingKey = (districts, uuid) => string.Format(\"padding.{0}.invoke\", districts),\n".Basic<ServiceMeta>(meta => meta.Name)
                            ).Satisfy(meta => meta.Type == ServiceType.Service))
                        .Satisfy(meta => meta.Scope == ServiceScope.InterServer)
                    );

            var returnBindKey =
                Generator.GenSelect(
                    Generator.GenSelect(
                        "ReturnBindingKey = (districts, uuid) => string.Format(\"{{0}}/{0}/return/{{1}}\", districts, uuid),\n"
                            .Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Divisional)
                        ,
                        "ReturnBindingKey = (districts, uuid) => string.Format(\"padding/{0}/return/{{0}}\", uuid),\n"
                            .Basic<ServiceMeta>(meta => meta.Name))
                        .Satisfy(meta => meta.Scope == ServiceScope.ClientToServer && client)
                    , Generator.GenSelect(
                        Generator.GenSelect(
                            "ReturnBindingKey = (districts, uuid) => string.Format(\"{{0}}.{0}.return.{{1}}\", districts, uuid),\n"
                                .Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Divisional)
                            ,
                            "ReturnBindingKey = (districts, uuid) => string.Format(\"padding.{0}.return.{{1}}\", districts, uuid),\n"
                                .Basic<ServiceMeta>(meta => meta.Name)
                            ).Satisfy(meta => meta.Type == ServiceType.Service))
                        .Satisfy(meta => meta.Scope == ServiceScope.InterServer)

                    );

            var returnRoutingKey =
                Generator.GenSelect(
                    Generator.GenSelect(
                        "ReturnRoutingKey = (districts, uuid) => string.Format(\"{{0}}.{0}.return.{{1}}\", districts, uuid),\n"
                            .Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Divisional)
                        , "ReturnRoutingKey = (districts, uuid) => string.Format(\"padding.{0}.return.{{0}}\", uuid),\n"
                            .Basic<ServiceMeta>(meta => meta.Name))
                        .Satisfy(meta => meta.Scope == ServiceScope.ClientToServer && !client)
                    , Generator.GenSelect(
                        Generator.GenSelect(
                            "ReturnRoutingKey = (districts, uuid) => string.Format(\"{{0}}.{0}.return.{{1}}\", districts, uuid),\n"
                                .Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Divisional)
                            ,
                            "ReturnRoutingKey = (districts, uuid) => string.Format(\"padding.{0}.return.{{1}}\", districts, uuid),\n"
                                .Basic<ServiceMeta>(meta => meta.Name)
                            ).Satisfy(meta => meta.Type == ServiceType.Service))
                        .Satisfy(meta => meta.Scope == ServiceScope.InterServer));

            var returnExchange =
                Generator.GenSelect(
                    "ReturnExchangeName = () => \"amq.topic\",\n".Unit<ServiceMeta>()
                        .Satisfy(meta => meta.Scope == ServiceScope.ClientToServer && !client),
                    "ReturnExchangeName = () => \"{0}.return\",\n".Basic<ServiceMeta>(meta => meta.Name)
                        .Satisfy(meta => meta.Scope == ServiceScope.InterServer)
                    ).Satisfy(meta => meta.Type == ServiceType.Service);

            var delegateExchange =
                Generator.GenSelect(
                    "DelegateExchangeName = () => \"amq.topic\",\n".Unit<ServiceMeta>()
                        .Satisfy(meta => meta.Scope == ServiceScope.ServerToClient && !client)
                    ,
                    "DelegateExchangeName = () => \"{0}.notify\",\n".Basic<ServiceMeta>(meta => meta.Name)
                        .Satisfy(meta => meta.Type == ServiceType.Notify && !client)
                    ,
                    "DelegateExchangeName = () => \"{0}.invoke\",\n".Basic<ServiceMeta>(meta => meta.Name)
                        .Satisfy(meta => meta.Type == ServiceType.Service && meta.Scope == ServiceScope.InterServer && !client));

            var implExchange =
                Generator.GenSelect(
                    "ImplExchangeName = () => \"{0}.notify\",\n".Basic<ServiceMeta>(meta => meta.Name)
                        .Satisfy(meta => meta.Type == ServiceType.Notify && !client),
                    "ImplExchangeName = () => \"{0}.invoke\",\n".Basic<ServiceMeta>(meta => meta.Name)
                        .Satisfy(meta => meta.Type == ServiceType.Service && !client));

            var implQueueName =
                Generator.GenSelect(
                    "ImplQueueName = districts=>string.Format(\"{{0}}.{0}.invoke\", districts),\n"
                        .Basic<ServiceMeta>(
                            meta => serviceName.Code(meta)).Satisfy(meta => meta.Divisional)
                    , "ImplQueueName = districts => string.Format(\"padding.{0}.invoke\"),\n".Basic<ServiceMeta>(
                        meta => serviceName.Code(meta)))
                    .Satisfy(meta => meta.Type == ServiceType.Service && !client);

            var publishKey =
                Generator.GenSelect(
                    "PublishKey = (districts, uuid) => string.Format(\"{{0}}/{0}/invoke\", districts),\n"
                        .Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Divisional)
                    ,
                    "PublishKey = (districts, uuid) => string.Format(\"padding/{0}/invoke\"),\n"
                        .Basic<ServiceMeta>(meta => meta.Name))
                    .Satisfy(meta => meta.Scope == ServiceScope.ClientToServer && client);

            var subscribeKey =
                Generator.GenSelect(
                    "SubscribeKey = (districts, uuid) => string.Format(\"{{0}}/{0}/sync/{{1}}\", districts, uuid),\n"
                        .Basic<ServiceMeta>(meta => meta.Name)
                        .Satisfy(meta => meta.Type == ServiceType.Sync && meta.Scope == ServiceScope.ServerToClient && client)
                    ,
                    Generator.GenSelect(
                        "SubscribeKey = (districts, uuid) => string.Format(\"{{0}}/{0}/return/{{1}}\", districts, uuid),\n"
                        .Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Divisional)
                        ,
                        "SubscribeKey = (districts, uuid) => string.Format(\"padding/{0}/return/{{0}}\", uuid),\n"
                        .Basic<ServiceMeta>(meta => meta.Name))
                    .Satisfy(meta => meta.Scope == ServiceScope.ClientToServer && client));

            var zkServicePath =
                Generator.GenSelect(
                    "ServicePath = (districts) => string.Format(\"/{{0}}/{{1}}\", districts, {0}),\n"
.Basic<ServiceMeta>(meta => meta.Name).Satisfy(meta => meta.Divisional)
                    ,
                    "ServicePath = (districts) => string.Format(\"/global/{{1}}\", districts, {0}),\n"
                        .Basic<ServiceMeta>(meta => meta.Name));

            var gateServiceId =
                "ServiceId  = s => {0},"
                    .Basic<ServiceMeta>(meta => meta.Id);

            var amqpRule =
                "AmqpRule = new RoutingRule.AmqpRoutingRule()"
                    .Unit<ServiceMeta>()
                    .Append(
                        implBindingKey
                            .Append(delegateRoutingKey)
                            .Append(returnBindKey)
                            .Append(returnRoutingKey)
                            .Append(returnExchange)
                            .Append(delegateExchange)
                            .Append(implExchange)
                            .Append(implQueueName)
                            .Brace()).WithPostfix(",");

            var mqttRule =
    "MqttRule = new RoutingRule.MqttRoutingRule()"
        .Unit<ServiceMeta>()
        .Append(
            publishKey
                .Append(subscribeKey)
                .Brace()).WithPostfix(",");

            var gateRule =
"GateRule = new RoutingRule.GateRoutingRule()"
.Unit<ServiceMeta>()
.Append(
gateServiceId
    .Brace()).WithPostfix(",");

            var zkRule =
"ZkRule = new RoutingRule.ZkRoutingRule()"
.Unit<ServiceMeta>()
.Append(
zkServicePath
.Brace()).WithPostfix(",");

            var metaBuilder =
                "MetaData.SetServiceRoutingRule({0}, new RoutingRule()"
                .Basic<ServiceMeta>(meta => meta.Name)
                .Append(
                    amqpRule.SkipLine().Satisfy(_=>!client)
                    .Append(mqttRule.SkipLine().Satisfy(_=>client))
                    .Append(gateRule.SkipLine())
                    .Append(zkRule.SkipLine().Satisfy(_ => !client))
                    .Brace().WithPostfix(");"));

            var metaConstructor =
                implMetaInit.Many("\n").Append(metaBuilder.Many("\n")).Brace().WithPrefix("\nstatic AutoInit()");

            var metaCoder =
                autoInitName.Many("\n")
                .Append(metaConstructor)
                .Append(
                    "public static void Init(){}".Unit<IEnumerable<ServiceMeta>>())
                .Brace().WithPrefix("\npublic static class AutoInit");
            #endregion

            var serviceCoder =
                invokeGenCoder.Satisfy(_ => client)
                .Append(clientImplGenCoder.Satisfy(meta => client).SkipLine())
                .Append(serverImplGenCoder.Satisfy(meta => !client).SkipLine())
                .Append(delegateCoder.Satisfy(meta => (meta.Scope == ServiceScope.ClientToServer && client) || (meta.Scope != ServiceScope.ClientToServer && !client)).WithPostfix("\n"))
                .Append(dispatcherCoder.Satisfy(meta => (meta.Scope != ServiceScope.ServerToClient && !client) || (meta.Scope == ServiceScope.ServerToClient && client)).WithPostfix("\n"))
                .Append(serializerCoder.SkipLine());

            var serviceWithRegion =
                "#region {0}\n{1}#endregion".Basic<ServiceMeta>( serviceName.Code, serviceCoder.Code);

            return
                metaCoder.WithPostfix("\n")
                .Append(serviceWithRegion.Many("\n"));
        }

        public string GenServer(List<ServiceMeta> ast)
        {
            var topTemplate = ResourceLoader.Load("CommonServerTemplate.txt");
            var serverCoder = GenCoder(false);

            return string.Format(topTemplate, serverCoder.Code(ast), GenServiceVersion(ast));
        }

        public string GenClient(List<ServiceMeta> ast)
        {
            var topTemplate = ResourceLoader.Load("CommonClientTemplate.txt");
            var serverCoder = GenCoder(true);

            return string.Format(topTemplate, serverCoder.Code(ast.Where(meta => meta.Scope == ServiceScope.ClientToServer || meta.Scope == ServiceScope.ServerToClient)), GenServiceVersion(ast));
        }

        public string GenServiceVersion(IEnumerable<ServiceMeta> ast)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);

            foreach (var meta in ast.OrderBy(m => m.Name))
            {
                var stream = meta.GetVersionBytes();

                bw.Write(stream.GetBuffer(), 0, (int)stream.Length);
            }

            return string.Join("", MD5.Create().ComputeHash(ms.GetBuffer()).Select(b => b.ToString("x").PadLeft(2, '0')));
        }
    }
}
