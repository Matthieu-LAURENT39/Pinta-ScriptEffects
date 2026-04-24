using System.Reflection;
using Cairo;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Pinta.Core;

namespace ScriptEffects;

public sealed class ScriptEffect : BaseEffect
{
    private readonly IChromeService chrome;

    public ScriptEffect(IServiceProvider services)
    {
        chrome = services.GetService<IChromeService>();
        EffectData = new ScriptEffectData();
    }

    // TODO: Make this translatable once sure about the name.
    public override string Name => Translations.GetString("Script Effect");

    // TODO: Not sure if this is where it makes the most sense to put it
    public override string EffectMenuCategory => Translations.GetString("Render");

    public override bool IsConfigurable => true;

    public sealed override bool IsTileable => false;

    private ScriptEffectData Data => (ScriptEffectData)EffectData!;

    public override async Task<bool> LaunchConfiguration()
    {
        using ScriptEffectDialog dialog = new(chrome, Data);

        while (true)
        {
            Gtk.ResponseType response = await dialog.RunAsync();

            if (response == Gtk.ResponseType.Apply)
            {
                await dialog.TryCompileAndApply(); // Keep the dialog open for iterative previewing.
                continue;
            }

            if (response != Gtk.ResponseType.Ok)
            {
                if (!await dialog.ConfirmDiscardPendingDialogChanges())
                    continue;

                dialog.Destroy();
                return false;
            }

            if (await dialog.TryCompileAndApply())
            {
                dialog.Destroy();
                return true;
            }
        }
    }

    protected override void Render(ImageSurface source, ImageSurface destination, RectangleI roi)
    {
        Data.CompiledRender?.Invoke(source, destination, roi);
    }
}

public sealed class ScriptEffectData : EffectData
{
    public static readonly string DefaultScript = """
/// <summary>
/// Function called when the effect is rendered.
/// Write your rendering code here!
/// </summary>
/// <param name="source">The source image surface. It contains only the current layer.</param>
/// <param name="destination">The destination image surface. Output the processed image here.</param>
/// <param name="roi">The region of the image to render. This can be used to optimize rendering when only part of the image needs to be updated.
/// When you have an active selection, this will be the bounding box of the selection, as changes outside the selection are discarded.</param>
public static void Render(ImageSurface source, ImageSurface destination, RectangleI roi)
{
    // Example: a simple gradient effect that blends with the original image.
    ReadOnlySpan<ColorBgra> src = source.GetReadOnlyPixelData();
    Span<ColorBgra> dst = destination.GetPixelData();

    int width = source.Width;
    int height = source.Height;

    for (int y = roi.Y; y < roi.Bottom; y++)
    {
        int row = y * width;
        // Vertical gradient (R)
        byte r = (byte)(y * 255 / height);

        for (int x = roi.X; x < roi.Right; x++)
        {
            int i = row + x;
            // Horizontal gradient (G)
            byte g = (byte)(x * 255 / width);

            // Inverse average for B to add some variation
            byte b = (byte)(255 - ((r + g) / 2));

            // Blend the gradient with the original image with 50% opacity
            ColorBgra c = src[i];
            dst[i] = ColorBgra.FromBgra(
                (byte)((c.B + b) / 2),
                (byte)((c.G + g) / 2),
                (byte)((c.R + r) / 2),
                c.A
            );
        }
    }
}
""";

    private string scriptCode = DefaultScript;

    public string ScriptCode
    {
        get => scriptCode;
        set
        {
            if (value == scriptCode)
                return;

            scriptCode = value;
            FirePropertyChanged(nameof(ScriptCode));
        }
    }

    public Action<ImageSurface, ImageSurface, RectangleI>? CompiledRender { get; set; }

    public string? LastCompileError { get; set; }

    public override EffectData Clone()
    {
        ScriptEffectData clone = (ScriptEffectData)base.Clone();
        clone.scriptCode = scriptCode;
        clone.CompiledRender = CompiledRender;
        clone.LastCompileError = LastCompileError;
        return clone;
    }

    public override bool IsDefault => ScriptCode == DefaultScript;
}

/// <summary>
/// Compiles the user-provided script code into a delegate that can be invoked to render the effect.
/// </summary>
internal static class ScriptEffectCompiler
{
    private const string WrapperPrefix = """
using System;
using Cairo;
using Pinta.Core;

namespace ScriptEffects.Runtime;

public static class UserScript
{
""";

    private const string WrapperSuffix = """
}
""";

    /// <summary>
    /// Tries to compile the user-provided script code into a render method.
    /// If successful, the render delegate is returned.
    /// </summary>
    /// <param name="userCode">The user-provided script code</param>
    /// <param name="render">The compiled render delegate</param>
    /// <param name="errorMessage">The formatted error message, if compilation failed</param>
    /// <returns>True if compilation is successful, false otherwise</returns>
    public static bool TryCompile(
        string userCode,
        out Action<ImageSurface, ImageSurface, RectangleI>? render,
        out string? errorMessage)
    {
        render = null;
        errorMessage = null;

        string code = WrapperPrefix + Environment.NewLine + userCode + Environment.NewLine + WrapperSuffix;

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);

        // Reference all currently loaded assemblies that have a location to allow the user code to use any of Pinta's APIs.
        IEnumerable<MetadataReference> references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(assembly => assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(location => MetadataReference.CreateFromFile(location));

        // Compile the code into an in-memory assembly.
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: $"ScriptEffectsUserCode_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream peStream = new();
        EmitResult result = compilation.Emit(peStream);

        if (!result.Success)
        {
            int userCodeLineOffset = (WrapperPrefix + Environment.NewLine).Count(c => c == '\n');
            errorMessage = FormatDiagnostics(result.Diagnostics, userCodeLineOffset);
            return false;
        }

        // Load the compiled assembly and create a delegate for the render method.
        peStream.Position = 0;
        Assembly assembly = Assembly.Load(peStream.ToArray());
        Type? scriptType = assembly.GetType("ScriptEffects.Runtime.UserScript");
        MethodInfo? method = scriptType?.GetMethod(
            "Render",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(ImageSurface), typeof(ImageSurface), typeof(RectangleI)],
            modifiers: null);

        if (method is null)
        {
            errorMessage = "Could not find method: public static void Render(ImageSurface source, ImageSurface destination, RectangleI roi).";
            return false;
        }

        render = (Action<ImageSurface, ImageSurface, RectangleI>)Delegate.CreateDelegate(
            typeof(Action<ImageSurface, ImageSurface, RectangleI>),
            method);

        return true;
    }

    /// <summary>
    /// Formats the compiler diagnostics into a user-friendly error message.
    /// This is mainly to make sure the line numbers in the error messages correspond to the lines 
    /// in the editor, by accounting for the wrapper code's lines that the user can't see.
    /// </summary>
    /// <param name="diagnostics">The diagnostics to format</param>
    /// <param name="userCodeLineOffset">The number of lines in the wrapper code before the user code starts
    /// This is needed to map the line numbers to the editor's content.</param>
    /// <returns>The formatted error message string</returns>
    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics, int userCodeLineOffset)
    {
        // Only show errors
        // TODO: maybe have a way to show warnings?
        IEnumerable<Diagnostic> errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        return string.Join(
            Environment.NewLine,
            errors.Select(diagnostic =>
            {
                // If the error isn't in the user's code, just show the default message.
                if (!diagnostic.Location.IsInSource)
                    return diagnostic.ToString();

                var span = diagnostic.Location.GetLineSpan();
                // Subtract the wrapper lines so line numbers map to the editor's content.
                int line = Math.Max(1, span.StartLinePosition.Line + 1 - userCodeLineOffset);
                int column = span.StartLinePosition.Character + 1;
                return $"Line {line}, Col {column}: {diagnostic.GetMessage()}";
            }));
    }
}