using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ModelCodeC
{
    class ModelParser
    {
        public IEnumerable<ModelMeta> ParseSerialize(Assembly ass)
        {
            var models = ass.GetTypes()
                .Where(t => t.IsClass)
                .Select(TypeToModelMeta)
                .Where(meta => meta != null && meta.Serialize).ToList();

            CheckHierarchy(models);

            return models;
        }

        void CheckHierarchy(List<ModelMeta> models)
        {
            // return false only if the model is inherited from object or valueType
            // return true when the model is inherited from other model
            Func<ModelMeta, bool> derivedChecker =
                meta => !meta.Type.BaseType.Name.Equals("Object") && !meta.Type.BaseType.Name.Equals("ValueType");
            Func<ModelMeta, ModelMeta> topmostFinder =
                metaChild =>
                {
                    if (metaChild == null)
                    {
                        return null;
                    }

                    var cur = metaChild.Parent;

                    if (cur == null)
                    {
                        return null;
                    }

                    while (cur.Parent != null && derivedChecker(cur))
                    {
                        cur = cur.Parent;
                    }

                    return cur;
                };

            // set parent of every model
            foreach (var model in models)
            {
                if (derivedChecker(model)) //indicates that this model is a derived one
                {
                    var baseModel = models.FirstOrDefault(m => m.Type == model.Type.BaseType);
                    if (baseModel == null)
                    {
                        continue;
                    }

                    model.Parent = baseModel;
                }
            }

            // track all children of every topmost
            foreach (var node in models)
            {
                var topmost = topmostFinder(node);
                if (topmost == null)
                {
                    continue;
                }

                topmost.Children.Add(node);
            }

            foreach (var node in models)
            {
                if (node.Children.Count == 0)
                {
                    continue;
                }

                byte pos = 1;

                node.IsTopmostBase = true;
                node.IsHierarchy = true;
                node.HierarchyCode = pos++;
                foreach (var child in node.Children)
                {
                    foreach (var field in child.Type.GetFields())
                    {
                        if (field.DeclaringType != child.Type)
                        {
                            var fmeta = child.Fields.FirstOrDefault(f => f.Field.Equals(field));
                            if (fmeta != null)
                            {
                                fmeta.IsInherit = true;
                            }
                        }
                    }
                    
                    child.IsHierarchy = true;
                    child.HierarchyCode = pos++;
                }
            }
        }

        public IEnumerable<ModelMeta> ParseDataAccess(Assembly ass)
        {
            return ass.GetTypes()
    .Where(t => t.IsClass)
    .Select(TypeToModelMeta)
    .Where(meta => meta != null && (meta.RedisCacheName!=null || meta.MysqlTableName != null));
        }

        ModelMeta TypeToModelMeta(Type type)
        {
            var serializeAttribute = type.GetCustomAttributes()
                .FirstOrDefault(attr => attr.GetType().Name == "SerializeAttribute");
            var cacheAttribute = type.GetCustomAttributes()
                .FirstOrDefault(attr => attr.GetType().Name == "MainCacheAttribute");
            var persistenceAttribute = type.GetCustomAttributes()
                .FirstOrDefault(attr => attr.GetType().Name == "PersistenceAttribute");

            var props = cacheAttribute?.GetType().GetProperties();
            var fields = cacheAttribute?.GetType().GetFields();

            var modelMeta = new ModelMeta()
            {
                Type = type,
                Serialize = serializeAttribute!=null,
                RedisCacheName = (string)cacheAttribute?.GetType().GetProperty("CacheName").GetValue(cacheAttribute, null),
                MysqlTableName = (string)persistenceAttribute?.GetType().GetProperty("DbTableName").GetValue(persistenceAttribute, null),
            };

            modelMeta.Fields =
                type.GetFields().Select((f,p) => FieldToMeta(modelMeta, f, p)).ToList();

            if (cacheAttribute != null || persistenceAttribute != null)
            {
                modelMeta.Key =
                    modelMeta.Fields.FirstOrDefault(
                        f => f.Field.GetCustomAttributes(true).Any(a => a.GetType().Name == "KeyAttribute"));
            }

            return modelMeta;
        }

        FieldMeta FieldToMeta(ModelMeta model, FieldInfo field, int pos)
        {
            if (field.IsPublic)
            {
                var serializeAttribute = field.GetCustomAttributes()
                .FirstOrDefault(attr => attr.GetType().Name == "FieldIndexAttribute");
                return new FieldMeta()
                {
                    Field = field,
                    ModelMeta = model,
                    Position = (uint)pos + 1,
                    SerializeIndex = (uint)(serializeAttribute!=null? (byte)serializeAttribute.GetType().GetProperty("Index").GetValue(serializeAttribute, null):0),
                };
            }

            return null;
        }
    }
}
