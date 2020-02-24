namespace EightyDecibel.AsyncNats.Rpc
{
    using EightyDecibel.AsyncNats.Messages;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Threading.Tasks;

    internal delegate Task InvokeAsyncDelegate(ReadOnlyMemory<byte> payload);
    internal delegate byte[] SerializeDelegate(Task result);
    internal delegate byte[] InvokeDelegate(ReadOnlyMemory<byte> payload);

    internal static class NatsServerGenerator<TContract>
    {
        public static IReadOnlyDictionary<string, (MethodInfo, MethodInfo)> AsyncMethods { get; private set; }
        public static IReadOnlyDictionary<string, MethodInfo> SyncMethods { get; private set; }

        private static Type _type;

        static NatsServerGenerator()
        {
            if (!typeof(TContract).IsInterface) throw new Exception("TContract must be an interface");

            var typeBuilder = NatsClientAssembly.DefineClassType($"{typeof(TContract).Name}Server");
            typeBuilder.SetParent(typeof(NatsServerProxy<TContract>));

            CreateConstructor(typeBuilder);
            var (asyncMethods, syncMethods) = CreateMethods(typeBuilder);

            _type = typeBuilder.CreateType();

            // Link MethodBuilders -> Runtime Methods (otherwise can't make delegates from them)
            foreach(var pair in asyncMethods.ToArray())
            {
                var invoke = _type.GetMethod(pair.Value.invoke.Name, pair.Value.invoke.GetParameters().Select(p => p.ParameterType).ToArray());
                var serialize = _type.GetMethod(pair.Value.serialize.Name, pair.Value.serialize.GetParameters().Select(p => p.ParameterType).ToArray());

                asyncMethods[pair.Key] = (invoke, serialize);
            }
            foreach (var pair in syncMethods.ToArray())
            {
                syncMethods[pair.Key] = _type.GetMethod(pair.Value.Name, pair.Value.GetParameters().Select(p => p.ParameterType).ToArray());
            }

            AsyncMethods = asyncMethods;
            SyncMethods = syncMethods;
        }

        public static NatsServerProxy<TContract> CreateServerProxy(NatsConnection parent, string subject, string? queueGroup, string subscriptionId, INatsSerializer serializer, TContract contract, TaskScheduler? taskScheduler)
        {
            var result = Activator.CreateInstance(_type, parent, subject, queueGroup, subscriptionId, serializer, contract, taskScheduler, AsyncMethods, SyncMethods);
            return (NatsServerProxy<TContract>) result;
        }

        static private void CreateConstructor(TypeBuilder typeBuilder)
        {
            var baseConstructor = typeof(NatsServerProxy<TContract>).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0];
            var parameters = baseConstructor.GetParameters().Select(p => p.ParameterType).ToArray();
            var constructor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, parameters);
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            for (var i = 0; i < parameters.Length; i++)
                il.Emit(OpCodes.Ldarg, i + 1);

            il.Emit(OpCodes.Call, baseConstructor);
            il.Emit(OpCodes.Ret);
        }

        static private (Dictionary<string, (MethodInfo invoke, MethodInfo serialize)>, Dictionary<string, MethodInfo>) CreateMethods(TypeBuilder typeBuilder)
        {
            var asyncMethods = new Dictionary<string, (MethodInfo, MethodInfo)>();
            var syncMethods = new Dictionary<string, MethodInfo>();
            foreach (var contractMethod in typeof(TContract).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (contractMethod.ReturnType == typeof(Task) || (contractMethod.ReturnType.IsGenericType && contractMethod.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)))
                {
                    var invoke = CreateAsyncMethod(typeBuilder, contractMethod);
                    var serialize = CreateSerializeDelegate(typeBuilder, contractMethod);
                    asyncMethods.Add(contractMethod.Name, (invoke, serialize));
                }
                else
                {
                    syncMethods.Add(contractMethod.Name, CreateSyncMethod(typeBuilder, contractMethod));
                }
            }
            
            return (asyncMethods, syncMethods);
        }
    
        static private MethodBuilder CreateSyncMethod(TypeBuilder typeBuilder, MethodInfo contractMethod)
        {
            var serverMethod = typeBuilder.DefineMethod(
                $"{contractMethod.Name}",
                MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual,
                typeof(byte[]),
                new[] { typeof(ReadOnlyMemory<byte>) });

            var il = serverMethod.GetILGenerator();
            WriteDeserializeAndCall(contractMethod, il);
            WriteSerialize(il, contractMethod.ReturnType);
            return serverMethod;
        }

        static private MethodBuilder CreateAsyncMethod(TypeBuilder typeBuilder, MethodInfo contractMethod)
        {
            var serverMethod = typeBuilder.DefineMethod(
                $"{contractMethod.Name}",
                MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual,
                typeof(Task),
                new[] { typeof(ReadOnlyMemory<byte>) });

            var il = serverMethod.GetILGenerator();
            WriteDeserializeAndCall(contractMethod, il);
            il.Emit(OpCodes.Ret);
            return serverMethod;
        }

        static private MethodBuilder CreateSerializeDelegate(TypeBuilder typeBuilder, MethodInfo contractMethod)
        {
            var serverMethod = typeBuilder.DefineMethod(
                $"Serialize{contractMethod.Name}",
                MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual,
                typeof(byte[]),
                new[] { typeof(Task) });

            var type = typeof(void);
            var il = serverMethod.GetILGenerator();
            if (contractMethod.ReturnType != typeof(Task))
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Castclass, contractMethod.ReturnType);
                il.Emit(OpCodes.Callvirt, contractMethod.ReturnType.GetProperty("Result").GetGetMethod());

                type = contractMethod.ReturnType.GetGenericArguments()[0];
             }
            WriteSerialize(il, type);
            return serverMethod;
        }

        static private void WriteDeserializeAndCall(MethodInfo contractMethod, ILGenerator il)
        {
            var requestType = GetRequestType(contractMethod);
            if (requestType != typeof(void))
            {
                var local = il.DeclareLocal(requestType);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, typeof(NatsServerProxy<TContract>).GetField("_serializer", BindingFlags.NonPublic | BindingFlags.Instance));
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Callvirt, typeof(INatsSerializer).GetMethod("Deserialize").MakeGenericMethod(requestType));
                il.Emit(OpCodes.Stloc, local.LocalIndex);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, typeof(NatsServerProxy<TContract>).GetField("_contract", BindingFlags.NonPublic | BindingFlags.Instance));
                for (var i = 0; i < contractMethod.GetParameters().Length; i++)
                {
                    il.Emit(OpCodes.Ldloc_0);
                    il.Emit(OpCodes.Callvirt, requestType.GetProperty($"P{i + 1}").GetGetMethod());
                }
            }
            else 
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, typeof(NatsServerProxy<TContract>).GetField("_contract", BindingFlags.NonPublic | BindingFlags.Instance));
            }
            il.Emit(OpCodes.Callvirt, contractMethod);
        }

        static private void WriteSerialize(ILGenerator il, Type returnType)
        {
            var isVoid = returnType == typeof(void);
            var responseType = isVoid ? typeof(NatsServerResponse) : typeof(NatsServerResponse<>).MakeGenericType(returnType);
            var constructor = responseType.GetConstructor(isVoid ? Type.EmptyTypes : new[] { returnType });
            var responseLocal = il.DeclareLocal(responseType);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Stloc, responseLocal.LocalIndex);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, typeof(NatsServerProxy<TContract>).GetField("_serializer", BindingFlags.NonPublic | BindingFlags.Instance));
            il.Emit(OpCodes.Ldloc, responseLocal.LocalIndex);
            il.Emit(OpCodes.Callvirt, typeof(INatsSerializer).GetMethod("Serialize").MakeGenericMethod(responseType));
            il.Emit(OpCodes.Ret);
        }

        static private Type GetRequestType(MethodInfo contractMethod)
        {
            var parameters = contractMethod.GetParameters();
            if (parameters.Length == 0) return typeof(void);
            return NatsClientRequest.GetNatsClientRequestType(parameters.Select(p => p.ParameterType).ToArray());
        }
    }
}
