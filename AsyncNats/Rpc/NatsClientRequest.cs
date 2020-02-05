namespace EightyDecibel.AsyncNats.Rpc
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.Serialization;

    internal class NatsClientRequest
    {
        private static object _syncLock = new object();
        private static Dictionary<int, Type> _requestTypes = new Dictionary<int, Type>();

        public static Type GetNatsClientRequestType(Type[] types)
        {
            if (types.Length == 0) throw new Exception("Creating empty NatsClientRequest is not possible");
            var type = GetNatsClientRequestType(types.Length);
            return type.MakeGenericType(types);
        }

        private static Type GetNatsClientRequestType(int count)
        {
            lock (_syncLock)
            {
                if (_requestTypes.TryGetValue(count, out var type)) return type;

                var typeBuilder = NatsClientAssembly.DefineClassType("NatsClientRequest" + count);
                typeBuilder.SetCustomAttribute(new CustomAttributeBuilder(typeof(DataContractAttribute).GetConstructor(Type.EmptyTypes), Array.Empty<object>()));
                var types = typeBuilder.DefineGenericParameters(Enumerable.Range(1, count).Select(x => $"TP{x}").ToArray());
                var fields = BuildProperties(typeBuilder, types);
                var defaultConstructor = typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
                var constructor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, types);
                var il = constructor.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, defaultConstructor);
                for (var i = 0; i < count; i++)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg, i + 1);
                    il.Emit(OpCodes.Stfld, fields[i]);
                }
                il.Emit(OpCodes.Ret);

                return _requestTypes[count] = typeBuilder.CreateType();
            }
        }

        private static FieldBuilder[] BuildProperties(TypeBuilder typeBuilder, GenericTypeParameterBuilder[] types)
        {
            var fields = new FieldBuilder[types.Length];
            for (var i = 0; i < types.Length; i++)
            {
                var property = typeBuilder.DefineProperty($"P{i + 1}", PropertyAttributes.None, types[i], null);
                property.SetCustomAttribute(new CustomAttributeBuilder(typeof(DataMemberAttribute).GetConstructor(Type.EmptyTypes), Array.Empty<object>(), new[] { typeof(DataMemberAttribute).GetProperty("Order") }, new object[] { i + 1 }));
                fields[i] = typeBuilder.DefineField($"_p{i + 1}", types[i], FieldAttributes.Private);

                var attr = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
                var getter = typeBuilder.DefineMethod($"get_P{i + 1}", attr, types[i], Type.EmptyTypes);
                var il = getter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, fields[i]);
                il.Emit(OpCodes.Ret);

                var setter = typeBuilder.DefineMethod($"set_P{i + 1}", attr, null, new[] { types[i] });
                il = setter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, fields[i]);
                il.Emit(OpCodes.Ret);

                property.SetGetMethod(getter);
                property.SetSetMethod(setter);
            }
            return fields;
        }
    }
}
