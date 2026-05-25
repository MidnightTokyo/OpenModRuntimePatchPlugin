using Autofac;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API;
using OpenMod.API.Plugins;
using OpenMod.Core.Helpers;
using OpenMod.Core.Plugins;
using OpenMod.Runtime;
using OpenMod.Unturned.Plugins;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

// For more, visit https://openmod.github.io/openmod-docs/devdoc/guides/getting-started.html

[assembly: PluginMetadata("OpenModRuntimePatch", DisplayName = "OpenMod.Runtime Patch")]

namespace OpenModRuntimePatch
{
    public class Main : OpenModUniversalPlugin
    {
        private readonly MethodInfo m_ShutdownAsyncOrig;
        private readonly MethodInfo m_ShutdownAsyncTranspiler;

        public Main(IServiceProvider serviceProvider) : base(serviceProvider)
        {
            Harmony.DEBUG = true;
            m_ShutdownAsyncOrig = typeof(Runtime).GetMethod(nameof(Runtime.ShutdownAsync), BindingFlags.Public | BindingFlags.Instance)
                .GetCustomAttribute<AsyncStateMachineAttribute>().StateMachineType.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);

            m_ShutdownAsyncTranspiler = typeof(Main).GetMethod(nameof(ShutdownAsyncTranspiler), BindingFlags.NonPublic | BindingFlags.Static);
        }

        protected override Task OnLoadAsync()
        {
            Harmony.Patch(m_ShutdownAsyncOrig, transpiler: new HarmonyMethod(m_ShutdownAsyncTranspiler));
            return Task.CompletedTask;
        }

        protected override Task OnUnloadAsync()
        {
            Harmony.Unpatch(m_ShutdownAsyncOrig, m_ShutdownAsyncTranspiler);
            return Task.CompletedTask;
        }

        private static IEnumerable<CodeInstruction> ShutdownAsyncTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codeInstructions = new List<CodeInstruction>(instructions);

            MethodInfo getHost = AccessTools.PropertyGetter(typeof(Runtime), "Host");
            int insertIndex = -1;

            for (int i = 0; i < codeInstructions.Count - 1; i++)
            {
                if (codeInstructions[i].Calls(getHost) && (codeInstructions[i + 1].opcode == OpCodes.Brfalse || codeInstructions[i + 1].opcode == OpCodes.Brfalse_S))
                {
                    insertIndex = i + 1;
                    break;
                }
            }

            if (insertIndex < 0)
                return instructions;

            List<CodeInstruction> newInstructions = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldloc_1),
                new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Runtime), "LifetimeScope")),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ResolutionExtensions), "Resolve", new Type[] { typeof(IComponentContext) }).MakeGenericMethod(typeof(IOpenModHost))),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DisposeHelper), "DisposeSyncOrAsync")),
                new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(Task), "Wait"))
            };

            codeInstructions.InsertRange(insertIndex, newInstructions);

            return codeInstructions;
        }
    }
}
