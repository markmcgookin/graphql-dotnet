using System;
using System.Collections.Generic;
using System.Linq;
using GraphQL.Introspection;

namespace GraphQL.Types
{
    public class GraphTypesLookup
    {
        private readonly Dictionary<string, GraphType> _types = new Dictionary<string, GraphType>();

        public GraphTypesLookup()
        {
            AddType<StringGraphType>();
            AddType<BooleanGraphType>();
            AddType<FloatGraphType>();
            AddType<IntGraphType>();
            AddType<IdGraphType>();

            AddType<NonNullGraphType<StringGraphType>>();
            AddType<NonNullGraphType<BooleanGraphType>>();
            AddType<NonNullGraphType<FloatGraphType>>();
            AddType<NonNullGraphType<IntGraphType>>();
            AddType<NonNullGraphType<IdGraphType>>();

            AddType<__Type>();
            AddType<__Field>();
            AddType<__EnumValue>();
            AddType<__InputValue>();
            AddType<__TypeKind>();
        }

        public void Clear()
        {
            _types.Clear();
        }

        public IEnumerable<GraphType> All()
        {
            return _types.Values;
        }

        public GraphType this[string typeName]
        {
            get
            {
                GraphType result;
                _types.TryGetValue(typeName, out result);
                return result;
            }
            set { _types[typeName] = value; }
        }

        public GraphType this[Type type]
        {
            get
            {
                var result = _types.FirstOrDefault(x => x.Value.GetType() == type);
                return result.Value;
            }
        }

        public IEnumerable<GraphType> FindImplemenationsOf(Type type)
        {
            // TODO: handle Unions
            return _types
                .Values
                .Where(t => t is ObjectGraphType)
                .Where(t => t.As<ObjectGraphType>().Interfaces.Any(i => (Type)i == type))
                .Select(x => x)
                .ToList();
        }

        public void AddType<TType>()
            where TType : GraphType, new()
        {
            var context = new TypeCollectionContext(
                type => (GraphType) Activator.CreateInstance(type),
                (name, type, _) =>
                {
                    _types[name] = type;
                    if (_ != null)
                    {
                        _.AddType(name, type, null);
                    }
                });

            AddType<TType>(context);
        }

        public void AddType<TType>(TypeCollectionContext context)
            where TType : GraphType
        {
            var instance = context.ResolveType(typeof (TType));
            AddType(instance, context);
        }

        public void AddType(GraphType type, TypeCollectionContext context)
        {
            if (type == null)
            {
                return;
            }

            var name = type.CollectTypes(context);
            _types[name] = type;

            type.Fields.Apply(field =>
            {
                AddTypeIfNotRegistered(field.Type, context);

                if (field.Arguments != null)
                {
                    field.Arguments.Apply(arg =>
                    {
                        AddTypeIfNotRegistered(arg.Type, context);
                    });
                }
            });

            if (type is ObjectGraphType)
            {
                var obj = (ObjectGraphType) type;
                obj.Interfaces.Apply(objectInterface =>
                {
                    AddTypeIfNotRegistered(objectInterface, context);

                    var interfaceInstance = this[objectInterface] as InterfaceGraphType;
                    if (interfaceInstance != null)
                    {
                        interfaceInstance.AddPossibleType(obj);

                        if (interfaceInstance.ResolveType == null && obj.IsTypeOf == null)
                        {
                            throw new ExecutionError((
                                "Interface type {0} does not provide a \"resolveType\" function" +
                                "and possible Type \"{1}\" does not provide a \"isTypeOf\" function.  " +
                                "There is no way to resolve this possible type during execution.")
                                .ToFormat(interfaceInstance, obj));
                        }
                    }
                });
            }

            if (type is UnionGraphType)
            {
                var union = (UnionGraphType) type;

                if (!union.Types.Any())
                {
                    throw new ExecutionError("Must provide types for Union {0}.".ToFormat(union));
                }

                union.Types.Apply(unionedType =>
                {
                    AddTypeIfNotRegistered(unionedType, context);

                    var objType = this[unionedType] as ObjectGraphType;

                    if (union.ResolveType == null && objType != null && objType.IsTypeOf == null)
                    {
                        throw new ExecutionError((
                            "Union type {0} does not provide a \"resolveType\" function" +
                            "and possible Type \"{1}\" does not provide a \"isTypeOf\" function.  " +
                            "There is no way to resolve this possible type during execution.")
                            .ToFormat(union, objType));
                    }

                    union.AddPossibleType(objType);
                });
            }
        }

        private void AddTypeIfNotRegistered(Type type, TypeCollectionContext context)
        {
            var foundType = this[type];
            if (foundType == null)
            {
                AddType(context.ResolveType(type), context);
            }
        }
    }
}