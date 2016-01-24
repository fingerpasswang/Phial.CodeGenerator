using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ServiceCodeC
{
    class ServiceParser
    {
        public List<ServiceMeta> Parse(Assembly ass)
        {
            return ass.GetTypes()
                .Where(t => t.IsInterface)
                .Select(ServiceToMeta)
                .Where(meta => meta != null)
                .ToList();
        }

        ServiceType ServiceTypeToEnum(Type type)
        {
            if (type.Name == "NotifyAttribute")
            {
                return ServiceType.Notify;
            }
            if (type.Name == "ServiceAttribute")
            {
                return ServiceType.Service;
            }
            if (type.Name == "SyncAttribute")
            {
                return ServiceType.Sync;
            }
            return ServiceType.None;
        }

        ServiceScope ServiceNameToEnum(string typeName)
        {
            if (typeName.StartsWith("IClient"))
            {
                return ServiceScope.ServerToClient;
            }
            if (typeName.EndsWith("ClientService"))
            {
                return ServiceScope.ClientToServer;
            }
            return ServiceScope.InterServer;
        }

        string TypeNameToName(string typeName, Type type)
        {
            if (type.Name == "NotifyAttribute")
            {
                return typeName.Substring(1, typeName.LastIndexOf("Notify")-1);
            }
            
            return typeName.Substring(1, typeName.LastIndexOf("Service") - 1);
        }

        ServiceMeta ServiceToMeta(Type type, int pos)
        {
            var test = type.GetCustomAttributes();
            var serviceAttr = type.GetCustomAttributes()
                .FirstOrDefault(attr => attr.GetType().BaseType?.Name == "ServiceAttribute" || attr.GetType().Name == "ServiceAttribute");

            if (serviceAttr == null)
            {
                return null;
            }

            var typeName = type.Name;

            var svcMeta = new ServiceMeta()
            {
                Id = (uint)pos+1,
                Name = TypeNameToName(typeName, serviceAttr.GetType()),
                Type = ServiceTypeToEnum(serviceAttr.GetType()),
                Scope = ServiceNameToEnum(typeName),
                Divisional = (bool)serviceAttr.GetType().GetProperty("Divisional").GetValue(serviceAttr, null),
                Multicast = serviceAttr.GetType().Name.Equals("SyncAttribute") && (bool)serviceAttr.GetType().GetProperty("Multicast").GetValue(serviceAttr, null),
            };

            svcMeta.Methods = type.GetMethods().Select((m, p) => MethodToMeta(svcMeta, m, p)).Where(meta => meta != null).ToList();

            return svcMeta;
        }
        MethodMeta MethodToMeta(ServiceMeta svcMeta, MethodInfo methodInfo, int pos)
        {
            return new MethodMeta()
            {
                Name = methodInfo.Name,
                Id = (uint)pos + 1,
                Parameters = methodInfo.GetParameters().Select((paraInfo, i) => new ParameterMeta() { Name = paraInfo.Name, Type = paraInfo.ParameterType, Position = (uint)i, }).ToList(),
                ReturnType = methodInfo.ReturnType,
                ServiceMeta = svcMeta,
            };
        }
    }
}
