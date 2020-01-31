namespace EightyDecibel.AsyncNats.Rpc
{
    using System.Reflection;
    using System.Reflection.Emit;
    using System.Runtime.CompilerServices;

    internal static class NatsClientAssembly
    {
        static NatsClientAssembly()
        {
            AssemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"EightyDecibel.AsyncNats.Client"), AssemblyBuilderAccess.Run);
            ModuleBuilder = AssemblyBuilder.DefineDynamicModule($"EightyDecibel.AsyncNats.Client.dll");

            // HACK: Allow access to `internal` classes from dynamically generated assembly.
            // https://www.strathweb.com/2018/10/no-internalvisibleto-no-problem-bypassing-c-visibility-rules-with-roslyn/
            var ignoreAccessChecksTo = new CustomAttributeBuilder(typeof(IgnoresAccessChecksToAttribute).GetConstructor(new[] { typeof(string) }), new[] { "EightyDecibel.AsyncNats" });
            AssemblyBuilder.SetCustomAttribute(ignoreAccessChecksTo);
        }

        public static TypeBuilder DefineClassType(string name)
        {
            return ModuleBuilder.DefineType($"{name}", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Serializable);
        }

        public static AssemblyBuilder AssemblyBuilder { get; private set; }
        public static ModuleBuilder ModuleBuilder { get; private set; }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
            AssemblyName = assemblyName;
        }

        public string AssemblyName { get; }
    }
}
