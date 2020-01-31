namespace EightyDecibel.AsyncNats.Rpc
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Threading.Tasks;

    internal static class NatsClientGenerator<TContract>
    {
        private static Type _type;

        static NatsClientGenerator()
        {
            if (!typeof(TContract).IsInterface) throw new Exception("TContract must be an interface");

            var typeBuilder = NatsClientAssembly.DefineClassType($"{typeof(TContract).Name}Client");
            typeBuilder.AddInterfaceImplementation(typeof(TContract));
            typeBuilder.SetParent(typeof(NatsClientProxy));

            CreateConstructor(typeBuilder);
            CreateMethods(typeBuilder);

            _type = typeBuilder.CreateType();
        }

        static private void CreateConstructor(TypeBuilder typeBuilder)
        {
            var constructor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[] { typeof(INatsConnection), typeof(string) });
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            var baseConstuctor = typeof(NatsClientProxy).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(INatsConnection), typeof(string) }, null);
            il.Emit(OpCodes.Call, baseConstuctor);
            il.Emit(OpCodes.Ret);
        }

        static private void CreateMethods(TypeBuilder typeBuilder)
        {
            foreach (var contractMethod in typeof(TContract).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var parameterTypes = contractMethod.GetParameters().Select(p => p.ParameterType).ToArray();
                var clientMethod = typeBuilder.DefineMethod(
                    contractMethod.Name,
                    MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual,
                    contractMethod.ReturnType,
                    parameterTypes);

                var responseType = contractMethod.ReturnType;
                var invokeMethod = "Invoke";
                if (responseType == typeof(Task))
                {
                    invokeMethod = "InvokeAsync";
                }
                else if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    responseType = responseType.GetGenericArguments()[0];
                    invokeMethod = "InvokeAsync";
                }

                var il = clientMethod.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, contractMethod.Name);

                var requestType = typeof(byte);
                if (parameterTypes.Length > 0)
                {
                    requestType = NatsClientRequest.GetNatsClientRequestType(parameterTypes);

                    for (var i = 0; i < parameterTypes.Length; i++)
                    {
                        il.Emit(OpCodes.Ldarg, i + 1);
                    }
                    il.Emit(OpCodes.Newobj, requestType.GetConstructor(parameterTypes));
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                }

                var requestMethod = responseType == typeof(void) ?
                    typeof(NatsClientProxy).GetMethod("InvokeVoid", BindingFlags.NonPublic | BindingFlags.Instance).MakeGenericMethod(requestType) :
                    typeof(NatsClientProxy).GetMethod(invokeMethod, BindingFlags.NonPublic | BindingFlags.Instance).MakeGenericMethod(requestType, responseType);
                il.Emit(OpCodes.Call, requestMethod);
                il.Emit(OpCodes.Ret);
            }
        }

        public static TContract GenerateClient(INatsConnection connection, string baseSubject)
        {
            return (TContract)Activator.CreateInstance(_type, connection, baseSubject);
        }
    }
}
