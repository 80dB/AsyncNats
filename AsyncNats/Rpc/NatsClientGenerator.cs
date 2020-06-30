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

        private static void CreateConstructor(TypeBuilder typeBuilder)
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

        private static void CreateMethods(TypeBuilder typeBuilder)
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
                var hasResult = true;
                var invokeMethod = "Invoke";
                if (responseType == typeof(Task))
                {
                    invokeMethod = "InvokeAsync";
                }
                else if (responseType == typeof(void))
                {
                    invokeMethod = "InvokeVoid";
                    hasResult = false;
                }
                else if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    responseType = responseType.GetGenericArguments()[0];
                    invokeMethod = "InvokeAsync";
                } 
                else if (responseType == typeof(ValueTask) || (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
                {
                    throw new Exception("ValueTask types are not supported");
                }

                if (contractMethod.GetCustomAttribute<NatsFireAndForgetAttribute>() != null)
                {
                    hasResult = false;

                    if (responseType == typeof(Task)) invokeMethod = "PublishAsync";
                    else if (responseType == typeof(void)) invokeMethod = "Publish";
                    else throw new Exception("NatsFireAndForget is only allowed for void/Task methods");
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

                var requestMethod = hasResult ?
                    typeof(NatsClientProxy).GetMethod(invokeMethod, BindingFlags.NonPublic | BindingFlags.Instance).MakeGenericMethod(requestType, responseType) :
                    typeof(NatsClientProxy).GetMethod(invokeMethod, BindingFlags.NonPublic | BindingFlags.Instance).MakeGenericMethod(requestType);
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
