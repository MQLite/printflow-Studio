using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Structural enforcement of Epic 11300 Part D2B's production policy: PrintFlow never
/// force-terminates Meitu.
/// </summary>
/// <remarks>
/// The source-token check catches shell/native wrappers, while the compiled-code check resolves
/// actual method calls and P/Invoke entry points. Keeping both matters: spelling checks alone can
/// miss an aliased or wrapped <see cref="Process.Kill()"/> call, and IL alone cannot establish the
/// meaning of a command string passed to an otherwise legitimate <see cref="Process.Start"/>.
/// </remarks>
public sealed class ForceTerminationPolicyBoundaryTests
{
    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => code.Value);

    /// <summary>
    /// No compiled production method calls <see cref="Process.Kill()"/> and no production
    /// P/Invoke imports a Windows process-termination entry point.
    /// </summary>
    [Fact]
    public void Compiled_product_has_no_force_termination_call_or_native_entry_point()
    {
        List<string> offenders = [];

        foreach (Assembly assembly in ProductAssemblies())
        {
            foreach (MethodBase method in MethodsOf(assembly))
            {
                foreach (MethodBase called in CalledMethods(method))
                {
                    if (called.DeclaringType == typeof(Process) &&
                        string.Equals(called.Name, nameof(Process.Kill), StringComparison.Ordinal))
                    {
                        offenders.Add($"{method.DeclaringType?.FullName}.{method.Name} -> Process.Kill");
                    }
                }

                if (method is MethodInfo info && info.GetCustomAttribute<DllImportAttribute>() is { } import)
                {
                    string entryPoint = import.EntryPoint ?? info.Name;
                    if (NativeTerminationEntryPoints.Contains(entryPoint, StringComparer.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{info.DeclaringType?.FullName}.{info.Name} -> {entryPoint}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "the production assemblies must contain no executable process-termination capability");
    }

    /// <summary>
    /// Production-owned member names expose no wrapper that a later Stop/timeout/unknown-state
    /// branch could call as an escalation step.
    /// </summary>
    [Fact]
    public void Product_capability_surface_names_no_force_termination_wrapper()
    {
        string[] destructiveNames = ["Kill", "Terminate", "ForceClose", "AbortProcess"];
        List<string> offenders = [];

        foreach (Assembly assembly in ProductAssemblies())
        {
            foreach (Type type in TypesOf(assembly))
            {
                if (destructiveNames.Any(name =>
                        type.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add(type.FullName ?? type.Name);
                }

                foreach (MemberInfo member in type.GetMembers(
                             BindingFlags.Public | BindingFlags.NonPublic |
                             BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (destructiveNames.Any(name =>
                            member.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        offenders.Add($"{type.FullName}.{member.Name}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "the product must not hide destructive process control behind a local wrapper");
    }

    /// <summary>
    /// The executable stop contract contains permissions only for the signed operation cancel,
    /// the signed export-surface cancel, ordinary input, retained state and validated success.
    /// </summary>
    [Fact]
    public void Stop_resolution_has_no_automatic_escalation_permission()
    {
        typeof(AutomationStopResolution).GetProperties()
            .Select(property => property.Name)
            .ShouldBe(
            [
                nameof(AutomationStopResolution.MayInvokeOperationCancel),
                nameof(AutomationStopResolution.MayCancelExportSurface),
                nameof(AutomationStopResolution.MayProduceAnyInput),
                nameof(AutomationStopResolution.Retained),
                nameof(AutomationStopResolution.PreservesValidatedSuccess),
            ], ignoreOrder: true);
    }

    /// <summary>
    /// Shell/native termination spellings remain absent from executable product source. This is
    /// the complementary guard for command strings that compiled call resolution cannot
    /// interpret semantically.
    /// </summary>
    [Theory]
    [InlineData("TerminateProcess")]
    [InlineData("NtTerminateProcess")]
    [InlineData("ZwTerminateProcess")]
    [InlineData("TerminateJobObject")]
    [InlineData("Process.Kill")]
    [InlineData(".Kill(")]
    [InlineData("taskkill")]
    [InlineData("Stop-Process")]
    [InlineData("Win32_Process")]
    [InlineData("CloseMainWindow")]
    [InlineData("WM_CLOSE")]
    [InlineData("SC_CLOSE")]
    public void Product_source_has_no_shell_or_window_termination_route(string bannedToken)
    {
        List<string> offenders = [];

        foreach (string project in ProductProjectNames)
        {
            foreach (string file in Directory.EnumerateFiles(
                         ProjectDirectory(project), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(file);
                for (int index = 0; index < lines.Length; index++)
                {
                    string trimmed = lines[index].TrimStart();
                    if (!trimmed.StartsWith("//", StringComparison.Ordinal) &&
                        !trimmed.StartsWith("///", StringComparison.Ordinal) &&
                        !trimmed.StartsWith("*", StringComparison.Ordinal) &&
                        lines[index].Contains(bannedToken, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{project}/{Path.GetFileName(file)}:{index + 1}: {trimmed}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            $"'{bannedToken}' would create an automatic route for closing or terminating Meitu");
    }

    private static string[] ProductProjectNames =>
        ["PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App"];

    private static string[] NativeTerminationEntryPoints =>
        ["TerminateProcess", "NtTerminateProcess", "ZwTerminateProcess", "TerminateJobObject"];

    private static Assembly[] ProductAssemblies() =>
    [
        typeof(ProcessingAttempt).Assembly,
        typeof(AutomationStopMode).Assembly,
        typeof(ProductionMeituProcessor).Assembly,
        typeof(SessionViewModel).Assembly,
    ];

    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    private static IEnumerable<MethodBase> MethodsOf(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (Type type in TypesOf(assembly))
        {
            foreach (ConstructorInfo constructor in type.GetConstructors(flags))
            {
                yield return constructor;
            }

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                yield return method;
            }
        }
    }

    private static IEnumerable<MethodBase> CalledMethods(MethodBase caller)
    {
        MethodBody? body;
        try
        {
            body = caller.GetMethodBody();
        }
        catch (InvalidOperationException)
        {
            yield break;
        }

        byte[]? il = body?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        int offset = 0;
        while (offset < il.Length)
        {
            short value = il[offset++] == 0xFE
                ? unchecked((short)(0xFE00 | il[offset++]))
                : il[offset - 1];

            if (!OpCodesByValue.TryGetValue(value, out OpCode code))
            {
                throw new InvalidOperationException(
                    $"Unknown IL opcode 0x{unchecked((ushort)value):X4} in {caller}.");
            }

            if (code.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(il, offset);
                MethodBase? called = ResolveMethod(caller, token);
                if (called is not null)
                {
                    yield return called;
                }
            }

            offset += OperandSize(code.OperandType, il, offset);
        }
    }

    private static MethodBase? ResolveMethod(MethodBase caller, int metadataToken)
    {
        try
        {
            Type[] typeArguments = caller.DeclaringType?.GetGenericArguments() ?? Type.EmptyTypes;
            Type[] methodArguments = caller.IsGenericMethod
                ? caller.GetGenericArguments()
                : Type.EmptyTypes;
            return caller.Module.ResolveMethod(metadataToken, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static int OperandSize(OperandType operandType, byte[] il, int operandOffset) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or
                OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or
                OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, operandOffset) * 4),
            _ => throw new InvalidOperationException($"Unknown IL operand type '{operandType}'."),
        };

    private static string ProjectDirectory(string project)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException("Could not locate the repository root.")
            : Path.Combine(current.FullName, "src", project);
    }
}
