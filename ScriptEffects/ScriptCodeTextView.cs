using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ScriptEffects;

// Text view that applies syntax highlighting to code
internal sealed class ScriptCodeTextView : Gtk.TextView
{
    private readonly Gtk.TextTag keywordTag;
    private readonly Gtk.TextTag typeTag;
    private readonly Gtk.TextTag stringTag;
    private readonly Gtk.TextTag numberTag;
    private readonly Gtk.TextTag commentTag;
    private readonly Gtk.TextTag preprocessorTag;

    public ScriptCodeTextView()
    {
        Monospace = true;
        Vexpand = true;
        Hexpand = true;

        keywordTag = CreateTag("script-keyword", new Gdk.RGBA { Red = 0.13f, Green = 0.35f, Blue = 0.74f, Alpha = 1f });
        typeTag = CreateTag("script-type", new Gdk.RGBA { Red = 0.09f, Green = 0.52f, Blue = 0.55f, Alpha = 1f });
        stringTag = CreateTag("script-string", new Gdk.RGBA { Red = 0.12f, Green = 0.55f, Blue = 0.25f, Alpha = 1f });
        numberTag = CreateTag("script-number", new Gdk.RGBA { Red = 0.70f, Green = 0.33f, Blue = 0.05f, Alpha = 1f });
        commentTag = CreateTag("script-comment", new Gdk.RGBA { Red = 0.45f, Green = 0.47f, Blue = 0.50f, Alpha = 1f });
        preprocessorTag = CreateTag("script-preprocessor", new Gdk.RGBA { Red = 0.53f, Green = 0.24f, Blue = 0.64f, Alpha = 1f });

        Gtk.TextBuffer buffer = Buffer!;
        Gtk.TextTagTable? tagTable = buffer.GetTagTable();
        tagTable?.Add(keywordTag);
        tagTable?.Add(typeTag);
        tagTable?.Add(stringTag);
        tagTable?.Add(numberTag);
        tagTable?.Add(commentTag);
        tagTable?.Add(preprocessorTag);

        buffer.OnChanged += HandleBufferChanged;
    }
    public string ScriptText
    {
        get => Buffer?.Text ?? string.Empty;
        set
        {
            if (Buffer is null)
                return;
            Buffer.Text = value;
            ApplySyntaxHighlighting();
        }
    }

    // Callback for when the text buffer changes.
    private void HandleBufferChanged(object? sender, EventArgs e)
    {
        ApplySyntaxHighlighting();
    }

    #region Syntax Highlighting
    /// <summary>
    /// Creates a text tag with the given name and foreground color.
    /// </summary>
    /// <param name="name">The name of the tag</param>
    /// <param name="color">The foreground color of the tag</param>
    /// <returns>The created text tag</returns>
    private static Gtk.TextTag CreateTag(string name, Gdk.RGBA color)
    {
        Gtk.TextTag tag = Gtk.TextTag.New(name);
        tag.ForegroundRgba = color;
        return tag;
    }

    /// <summary>
    /// Applies the given text tag to the specified span of text in the buffer.
    /// </summary>
    /// <param name="buffer">The text buffer to modify.</param>
    /// <param name="tag">The text tag to apply.</param>
    /// <param name="startOffset">The starting offset of the text span.</param>
    /// <param name="length">The length of the text span.</param>
    private static void ApplyTagToSpan(Gtk.TextBuffer buffer, Gtk.TextTag tag, int startOffset, int length)
    {
        buffer.GetIterAtOffset(out Gtk.TextIter start, startOffset);
        buffer.GetIterAtOffset(out Gtk.TextIter end, startOffset + length);
        buffer.ApplyTag(tag, start, end);
    }

    /// <summary>
    /// Applies syntax highlighting to the code text.
    /// This uses the Roslyn C# parser.
    /// </summary>
    private void ApplySyntaxHighlighting()
    {
        Gtk.TextBuffer? buffer = Buffer;
        if (buffer is null)
            return;

        string text = buffer.Text ?? string.Empty;
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text);
        SyntaxNode root = tree.GetRoot();

        buffer.GetBounds(out Gtk.TextIter start, out Gtk.TextIter end);
        buffer.RemoveAllTags(start, end);

        foreach (SyntaxToken token in root.DescendantTokens(descendIntoTrivia: true))
        {
            if (token.IsKeyword())
                ApplyTagToSpan(buffer, keywordTag, token.SpanStart, token.Span.Length);
            else if (token.IsKind(SyntaxKind.NumericLiteralToken))
                ApplyTagToSpan(buffer, numberTag, token.SpanStart, token.Span.Length);
            else if (token.IsKind(SyntaxKind.StringLiteralToken) || token.IsKind(SyntaxKind.CharacterLiteralToken) || token.IsKind(SyntaxKind.InterpolatedStringTextToken))
                ApplyTagToSpan(buffer, stringTag, token.SpanStart, token.Span.Length);
            else if (token.IsKind(SyntaxKind.IdentifierToken) && IsLikelyTypeIdentifier(token))
                ApplyTagToSpan(buffer, typeTag, token.SpanStart, token.Span.Length);

            HighlightTrivia(buffer, token.LeadingTrivia);
            HighlightTrivia(buffer, token.TrailingTrivia);
        }
    }

    /// <summary>
    /// Applies syntax highlighting tags to the given trivia list.
    /// Trivia includes things like comments and preprocessor directives that are not part of the main syntax.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="triviaList"></param>
    private void HighlightTrivia(Gtk.TextBuffer buffer, IEnumerable<SyntaxTrivia> triviaList)
    {
        foreach (SyntaxTrivia trivia in triviaList)
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                ApplyTagToSpan(buffer, commentTag, trivia.SpanStart, trivia.Span.Length);
            else if (trivia.IsKind(SyntaxKind.PreprocessingMessageTrivia) || trivia.IsDirective)
                ApplyTagToSpan(buffer, preprocessorTag, trivia.SpanStart, trivia.Span.Length);
        }
    }

    /// <summary>
    /// Determines if an identifier token is likely a type name, based on its syntax context.
    /// </summary>
    /// <param name="token">The syntax token to evaluate.</param>
    /// <returns><c>true</c> if the token is likely a type name; otherwise, <c>false</c>.</returns>
    private static bool IsLikelyTypeIdentifier(SyntaxToken token)
    {
        SyntaxNode? parent = token.Parent;
        return parent is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax
            && (parent.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax
                or Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax
                or Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax
                or Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclarationSyntax
                or Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax
                or Microsoft.CodeAnalysis.CSharp.Syntax.CastExpressionSyntax
                or Microsoft.CodeAnalysis.CSharp.Syntax.TypeOfExpressionSyntax);
    }
    #endregion
}